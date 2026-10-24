using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 封面图：与专辑卡片一样直接显示，不设加载态；失败时补一次歌单详情，仍拿不到才画占位。
/// </summary>
/// <remarks>
/// <para>
/// <b>只补歌单封面</b>：<see cref="PlaylistId"/> 为 0 时失败直接画占位 —— 专辑、歌手封面
/// 没有「列表与详情给不同地址」这个问题。
/// </para>
/// <para>
/// <b>成败读 <see cref="CoverImageCache"/> 的结果，而不是只看 <c>ImageOpened</c></b>：
/// 容器回收时会把已经解码完的位图挂到新的 <c>Image</c> 上，那个事件不会再触发。
/// 缓存位图的 <c>ImageFailed</c> 也可能在回收之后才到，会串到下一项，所以不拿它做判断。
/// </para>
/// </remarks>
public sealed partial class CoverImage : UserControl
{
    private bool _fallbackTried;
    private int _loadVersion;

    public CoverImage() => InitializeComponent();

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(CoverImage),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(CoverImage),
        new PropertyMetadata(Stretch.UniformToFill));

    public static readonly DependencyProperty SharedCoverKeyProperty = DependencyProperty.Register(
        nameof(SharedCoverKey),
        typeof(string),
        typeof(CoverImage),
        new PropertyMetadata(""));

    /// <summary>歌单 id。非 0 时，图片失败会补一次详情换地址。</summary>
    public static readonly DependencyProperty PlaylistIdProperty = DependencyProperty.Register(
        nameof(PlaylistId),
        typeof(long),
        typeof(CoverImage),
        new PropertyMetadata(0L));

    /// <summary>歌单的 sourceType，补详情时用；详情对不上 source 会回空对象。</summary>
    public static readonly DependencyProperty PlaylistSourceProperty = DependencyProperty.Register(
        nameof(PlaylistSource),
        typeof(int),
        typeof(CoverImage),
        new PropertyMetadata(0));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public string SharedCoverKey
    {
        get => (string)GetValue(SharedCoverKeyProperty);
        set => SetValue(SharedCoverKeyProperty, value);
    }

    public long PlaylistId
    {
        get => (long)GetValue(PlaylistIdProperty);
        set => SetValue(PlaylistIdProperty, value);
    }

    public int PlaylistSource
    {
        get => (int)GetValue(PlaylistSourceProperty);
        set => SetValue(PlaylistSourceProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CoverImage)d).BeginLoad();

    /// <summary>换图时复位占位与兜底记录。容器回收会频繁走到这里。</summary>
    private void BeginLoad()
    {
        var version = ++_loadVersion;

        _fallbackTried = false;
        Fallback.Visibility = Visibility.Collapsed;

        if (Source is null)
        {
            Cover.Source = null;
            Cover.Visibility = Visibility.Collapsed;

            // 列表项本来就没有封面；有 id 就补一次详情，补不到就保持空白。
            if (PlaylistId > 0)
            {
                _ = ResolveFallbackAsync(version, showPlaceholder: false);
            }

            return;
        }

        ShowImage(Source, version);
    }

    private void ShowImage(ImageSource image, int version)
    {
        Cover.Visibility = Visibility.Visible;
        Cover.Source = image;

        if (image is not BitmapImage bitmap)
        {
            return;
        }

        // 缓存里已有结果：成功什么都不用做，失败直接走兜底。
        if (CoverImageCache.TryGetState(bitmap, out var loaded))
        {
            if (!loaded)
            {
                _ = ResolveFallbackAsync(version, showPlaceholder: true);
            }

            return;
        }

        if (CoverImageCache.TryGetSettlement(bitmap) is { } settlement)
        {
            _ = WatchSettlementAsync(settlement, version);
        }
    }

    private async Task WatchSettlementAsync(Task<bool> settlement, int version)
    {
        var loaded = await settlement;

        if (version != _loadVersion)
        {
            return;
        }

        if (!loaded)
        {
            await ResolveFallbackAsync(version, showPlaceholder: true);
        }
    }

    /// <summary>
    /// 按 id 补一次详情，换用列表拿不到的可访问地址。
    /// </summary>
    /// <remarks>
    /// 新地址必须走 <c>Formats.CoverSource</c> —— 那里有内存与磁盘两层封面缓存；
    /// 直接 new <c>BitmapImage</c> 会绕过缓存，滚动回来又重新下一遍。
    /// </remarks>
    private async Task ResolveFallbackAsync(int version, bool showPlaceholder)
    {
        if (_fallbackTried)
        {
            if (showPlaceholder)
            {
                Cover.Visibility = Visibility.Collapsed;
                Fallback.Visibility = Visibility.Visible;
            }

            return;
        }

        _fallbackTried = true;

        var cover = Resolver() is { } resolver
            ? await resolver.ResolveAsync(PlaylistId, PlaylistSource)
            : null;

        // 等网络这段时间里容器可能已经装上了别的歌单。
        if (version != _loadVersion)
        {
            return;
        }

        if (cover is not null && Formats.CoverSource(cover) is { } image)
        {
            Fallback.Visibility = Visibility.Collapsed;
            ShowImage(image, version);
            return;
        }

        if (showPlaceholder)
        {
            Cover.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 缓存位图的失败不在这里处理（见类型说明）；只接非缓存来源。
    /// </summary>
    private void OnImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (Source is BitmapImage)
        {
            return;
        }

        if (PlaylistId > 0)
        {
            _ = ResolveFallbackAsync(_loadVersion, showPlaceholder: true);
            return;
        }

        Cover.Visibility = Visibility.Collapsed;
        Fallback.Visibility = Visibility.Visible;
    }

    private static PlaylistCoverResolver? Resolver() =>
        Application.Current.Resources.TryGetValue("BowenPlaylistCovers", out var value)
            ? value as PlaylistCoverResolver
            : null;
}
