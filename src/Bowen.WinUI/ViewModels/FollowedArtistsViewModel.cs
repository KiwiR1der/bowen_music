using Bowen.Core.Api;
using Bowen.Core.Api.Paging;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 「关注的歌手」页。
/// </summary>
/// <remarks>
/// <para>
/// 数据来自 <c>service/collect/7/list</c>（<c>source = 7</c>），与收藏歌单那条是同一族端点，
/// 见 <c>docs/collect-follow.md</c>。
/// </para>
/// <para>
/// <b>这条端点没有分页</b>（官方固定 <c>rn = 400</c>，一次全量），所以下面那个 fetch 委托
/// 拿到结果后直接把游标标到底 —— 详见方法上的注释。
/// </para>
/// </remarks>
public sealed class FollowedArtistsViewModel
{
    private readonly IBodianApi _api;

    public FollowedArtistsViewModel(IBodianApi api, ViewModeService viewMode,
        ILogger<FollowedArtistsViewModel>? logger = null, ICurrentAccount? account = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(viewMode);

        _api = api;
        ViewMode = viewMode;

        Artists = new PagedList<Artist>(
            FetchAsync,
            logger ?? NullLogger<FollowedArtistsViewModel>.Instance,
            "关注的歌手",
            "还没有关注的歌手。",
            // 「位歌手」当单位用：模板是「共 {n} {单位}」，得到的是「共 12 位歌手」。
            countUnit: "位歌手",
            account: account);
    }

    public PagedList<Artist> Artists { get; }

    /// <summary>
    /// 行列表还是封面卡片。状态在单例里 —— 与搜索结果那三个页签、收藏的专辑/歌单**共用同一个开关**。
    /// </summary>
    public ViewModeService ViewMode { get; }

    /// <summary>首次进入时调，已经加载过就什么都不做。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Artists.EnsureLoadedAsync(cancellationToken);

    /// <summary>
    /// 重新拉一次。给**换账号**用。
    /// </summary>
    /// <remarks>
    /// <b>不要拿它当「从歌手详情返回」的同步手段。</b> 那是它早先的用法。问题出在
    /// <c>ReloadAsync</c> 的动作顺序：先 <c>Items.Clear()</c>、再等一次网络请求才填回来。
    /// 中间那一轮布局里列表是空的，可用高度归零、滚动偏移被夹到 0，之后填回多少条都回不去 ——
    /// 位置没了，共享封面的返回落点也跟着没了，翻过一页之后必现。
    /// （「最近播放」那页也每次都清空重填，但它是清空之后**同步**加回去，所以位置不丢。）
    /// 返回路径现在走 <see cref="Remove"/>，见 <c>docs/animations.md</c> 的「返回时的滚动位置」。
    /// </remarks>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Artists.ReloadAsync(cancellationToken);

    /// <summary>
    /// 把一位歌手从列表里去掉，返回是否真的移除了。详情页里能改的只有「这一位还关不关注」，
    /// 所以只动这一项，不重拉。
    /// </summary>
    public bool Remove(long artistId) => Artists.Remove(artist => artist.Id == artistId);

    /// <summary>
    /// 一次取全部关注歌手，并声明这就是最后一页。
    /// </summary>
    /// <remarks>
    /// <b><c>Advance(0)</c> 是游标约定里「到底」的写法</b>（收到 0 条即 <c>Exhausted</c>）：
    /// 推进游标是 fetch 委托的责任，<see cref="PagedList{T}"/> 自己从不调它。
    /// 不标到底的话 <c>HasMore</c> 会停在 true，列表末尾的自动翻页就会再拉一次并追加一份重复的。
    /// </remarks>
    private async Task<PagedResult<Artist>> FetchAsync(PagedCursor cursor, CancellationToken cancellationToken)
    {
        var artists = await _api.GetFollowedArtistsAsync(cancellationToken).ConfigureAwait(true);

        cursor.Advance(0);

        return new PagedResult<Artist>(artists, cursor.Offset, cursor.PageSize, artists.Count);
    }
}
