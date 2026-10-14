using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「关注的歌手」页。侧栏「我的音乐」组下的一个根页。
/// </summary>
/// <remarks>
/// 数据来自 <c>service/collect/7/list</c>，一次全量、没有分页，见
/// <see cref="FollowedArtistsViewModel"/> 与 <c>docs/collect-follow.md</c>。
/// </remarks>
public sealed partial class FollowedArtistsPage : Page, INavigationAware, IAccountScopedView
{
    private readonly Func<Artist, ArtistDetailPage> _artistFactory;
    private readonly INavigationService _navigation;

    /// <summary>
    /// 本页刚打开过的那一个歌手详情页。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 详情页里能取消关注，回来时列表得跟着变。**但不能再重拉整个列表** ——
    /// 那会清空重建，滚动位置归零、容器的共享封面返回落点一起消失，翻过一页之后必现
    /// （见 <c>docs/animations.md</c> 的「返回时的滚动位置」）。
    /// </para>
    /// <para>
    /// 留着这个引用，回来时读它最终确定的关注态，只动那一位。根页实例被
    /// <see cref="NavigationService"/> 缓存（同一个页面实例反复显示），所以引用在
    /// 「压栈进详情 → 返回」这条路上一定还在。
    /// </para>
    /// </remarks>
    private ArtistDetailPage? _openedDetail;

    public FollowedArtistsPage(
        FollowedArtistsViewModel viewModel,
        INavigationService navigation,
        Func<Artist, ArtistDetailPage> artistFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(artistFactory);

        ViewModel = viewModel;
        _navigation = navigation;
        _artistFactory = artistFactory;

        InitializeComponent();
    }

    public FollowedArtistsViewModel ViewModel { get; }

    public void OnNavigatedTo()
    {
        if (_openedDetail is { } detail)
        {
            _openedDetail = null;

            // 只有「这次进来确实取消了关注」才动列表。IsFollowed 为 null 是「没能判定」
            // （未登录或读取失败），那时什么都不做 —— 宁可不删，也不能凭空抹掉一位关注的歌手。
            if (detail.ViewModel.IsFollowed is false)
            {
                ViewModel.Remove(detail.ViewModel.Artist.Id);
            }

            return;
        }

        _ = ViewModel.EnsureLoadedAsync();
    }

    /// <summary>换账号了：这一页是上一个账号关注的歌手。</summary>
    public Task OnAccountSwitchedAsync() => ViewModel.RefreshAsync();

    /// <summary>不需要收尾：列表是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 行列表与卡片网格里的点击。两个控件的事件参数形状一样，共用这一个处理函数。
    /// </summary>
    private void OnArtistClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not Artist artist)
        {
            return;
        }

        // 留住这个实例：返回时要读它最终确定的关注态，见 _openedDetail。
        var page = _artistFactory(artist);
        _openedDetail = page;
        _navigation.Navigate(page);
    }
}
