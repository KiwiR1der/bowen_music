using System.Runtime.CompilerServices;
using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.Controls;

/// <summary>标记页签内容；只动画内容根节点，不遍历或实现列表中的离屏项。</summary>
public static class Motion
{
    public static readonly DependencyProperty SharedCoverKeyProperty = DependencyProperty.RegisterAttached(
        "SharedCoverKey", typeof(string), typeof(Motion), new PropertyMetadata(""));
    public static string GetSharedCoverKey(DependencyObject element) => (string)element.GetValue(SharedCoverKeyProperty);
    public static void SetSharedCoverKey(DependencyObject element, string value) => element.SetValue(SharedCoverKeyProperty, value);

    public static readonly DependencyProperty ClipToBoundsProperty = DependencyProperty.RegisterAttached(
        "ClipToBounds", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnClipChanged));
    public static bool GetClipToBounds(DependencyObject element) => (bool)element.GetValue(ClipToBoundsProperty);
    public static void SetClipToBounds(DependencyObject element, bool value) => element.SetValue(ClipToBoundsProperty, value);
    private static void OnClipChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)args.NewValue)
        {
            element.SizeChanged += OnClipSizeChanged;
            ApplyClip(element);
        }
        else
        {
            element.SizeChanged -= OnClipSizeChanged;
            element.Clip = null;
        }
    }
    private static void OnClipSizeChanged(object sender, SizeChangedEventArgs args) => ApplyClip((FrameworkElement)sender);
    private static void ApplyClip(FrameworkElement element) => element.Clip = new RectangleGeometry
    {
        Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, element.ActualWidth), Math.Max(0, element.ActualHeight))
    };

    public static readonly DependencyProperty TabContentProperty = DependencyProperty.RegisterAttached(
        "TabContent", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    /// <summary>
    /// 封面图解码完成时淡入，代替「先露底色、图到了硬切」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CoverImageCache</c> 的约定是「先把 <c>BitmapImage</c> 挂到 <c>Image.Source</c>、
    /// 解码完成后再往里填」，所以卡片封面一直是硬切出来的。这里在源尚未解码时先把元素遮住
    /// （<see cref="AppMotion.Hide"/>），等 <c>ImageOpened</c> 到了再淡入。
    /// </para>
    /// <para>
    /// <b>已经就绪的源不淡入。</b> 缓存命中、虚拟化容器复用同一张已解码的图，都直接落位，
    /// 否则滚动一遍列表每张卡都闪一次。源为 <c>null</c> 时也直接落位：那一刻 <c>Image</c>
    /// 本来就是空的，露出来的是外层 Border 的底色，与不做这个动画时的表现一致。
    /// </para>
    /// <para>
    /// <b>不要挂在详情页页头的封面上。</b> 那是共享封面动画的目标元素，两边同时动会把对方
    /// 正在跑的合成动画 <c>Reset</c> 掉。这个属性是给列表卡片用的。
    /// </para>
    /// <para>
    /// 已知边界：来源卡片正被共享封面动画当作飞行起点（元素被临时遮住，约 420 ms）时，
    /// 若那张图恰好在窗口内解码完成，它会提前淡入一下，动画结束才恢复一致。
    /// 要同时满足「图没缓存」和「正好落在这个窗口里」—— 而点得动的卡片封面必然已经解码完成，
    /// 所以这条实际上很难发生，暂不为此加锁。
    /// </para>
    /// </remarks>
    public static readonly DependencyProperty CoverFadeInProperty = DependencyProperty.RegisterAttached(
        "CoverFadeIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnCoverFadeInChanged));

    public static bool GetCoverFadeIn(DependencyObject element) => (bool)element.GetValue(CoverFadeInProperty);

    public static void SetCoverFadeIn(DependencyObject element, bool value) => element.SetValue(CoverFadeInProperty, value);

    private sealed class CoverFade
    {
        public bool Hidden;
        public long SourceToken;
        public RoutedEventHandler? Opened;
        public ExceptionRoutedEventHandler? Failed;
    }

    private static readonly ConditionalWeakTable<Image, CoverFade> CoverFades = new();

    private static void OnCoverFadeInChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (!(bool)args.NewValue)
        {
            if (CoverFades.TryGetValue(image, out var existing))
            {
                image.ImageOpened -= existing.Opened;
                image.ImageFailed -= existing.Failed;
                image.UnregisterPropertyChangedCallback(Image.SourceProperty, existing.SourceToken);
                CoverFades.Remove(image);
            }

            AppMotion.Reset(image);
            return;
        }

        // 拆开写：lambda 里要引用 state 自己，放在初始化器里编译器不让。
        var state = new CoverFade();
        state.Opened = (_, _) =>
        {
            // 只有「被我们遮住、又在等这一张图」时才淡入；已经落位的源迟到的通知不重复动画。
            if (!state.Hidden)
            {
                return;
            }

            state.Hidden = false;
            _ = AppMotion.RevealAsync(image, AppMotion.FadeIn);
        };
        state.Failed = (_, _) => SyncCoverFade(image);
        state.SourceToken = image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => SyncCoverFade(image));
        image.ImageOpened += state.Opened;
        image.ImageFailed += state.Failed;
        CoverFades.Add(image, state);
        SyncCoverFade(image);
    }

    private static void SyncCoverFade(Image image)
    {
        var pending = image.Source switch
        {
            // 缓存命中时拿到的是同一个已解码的实例，直接显示。
            BitmapImage bitmap => bitmap.PixelWidth == 0,
            _ => false,
        };

        if (!CoverFades.TryGetValue(image, out var state))
        {
            return;
        }

        state.Hidden = pending;
        if (pending)
        {
            AppMotion.Hide(image);
        }
        else
        {
            AppMotion.Reset(image);
        }
    }

    public static bool GetTabContent(DependencyObject element) => (bool)element.GetValue(TabContentProperty);
    public static void SetTabContent(DependencyObject element, bool value) => element.SetValue(TabContentProperty, value);

    internal static void AnimateTabs(DependencyObject root, int direction)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && GetTabContent(child))
            {
                if (element.Visibility == Visibility.Visible)
                    _ = AppMotion.EnterAsync(element, direction * 30, direction == 0 ? 12 : 0);
                else AppMotion.Reset(element);
                continue;
            }
            // 列表项不属于页面结构，跳过虚拟化的内容树。
            if (child is Microsoft.UI.Xaml.Controls.ItemsControl) continue;
            AnimateTabs(child, direction);
        }
    }
}
