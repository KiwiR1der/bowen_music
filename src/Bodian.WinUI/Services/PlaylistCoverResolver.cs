using System.Collections.Concurrent;
using Bodian.Core.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.Services;

/// <summary>
/// 列表里的歌单封面可能是死链，失败时按 id 问一次详情换用详情给的地址。
/// </summary>
/// <remarks>
/// <para>
/// 列表与详情返回的不是同一个文件：列表常给 QQ 音乐 CDN 的 <c>_o.jpg</c>（404），
/// 详情给的是带签名的可访问地址。两边的 id 与 source 都在列表项上，所以补一次成本很小。
/// </para>
/// <para>
/// <b>结果按 (id, source) 缓存，失败也缓存</b>：卡片滚出视野再滚回来不重复请求，
/// 同一页里同一位歌手的坏封面也只问一次。同时在途的请求限 4 个，避免一屏卡片一起打服务端。
/// </para>
/// </remarks>
internal sealed class PlaylistCoverResolver
{
    private readonly IBodianApi _api;
    private readonly ILogger<PlaylistCoverResolver> _logger;
    private readonly ConcurrentDictionary<(long Id, int Source), Task<Uri?>> _cache = new();
    private readonly SemaphoreSlim _gate = new(4);

    public PlaylistCoverResolver(IBodianApi api, ILogger<PlaylistCoverResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<PlaylistCoverResolver>.Instance;
    }

    /// <summary>取可用的封面地址；拿不到返回 <c>null</c>。</summary>
    public Task<Uri?> ResolveAsync(long playlistId, int source)
    {
        // source 是列表项自带的 sourceType。0 / 负数拼不出有效请求，直接放弃。
        if (playlistId <= 0 || source <= 0)
        {
            return Task.FromResult<Uri?>(null);
        }

        return _cache.GetOrAdd((playlistId, source), key => ResolveCoreAsync(key.Id, key.Source));
    }

    private async Task<Uri?> ResolveCoreAsync(long playlistId, int source)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            // 列表项的 sourceType 是「在哪个列表里看到的」，不一定等于详情要的 source：
            // 收藏列表里的条目用 4 能取到元数据，个别条目（原来源是 13 的）却只有 pic 为空。
            // 所以按「它自己的 → 账号歌单 5 → 发现页 13」依次试，拿到封面就停。
            foreach (var candidate in Candidates(source))
            {
                var info = await _api.GetPlaylistInfoAsync(playlistId, candidate).ConfigureAwait(false);

                if (info?.CoverImage is { } cover)
                {
                    return cover;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "补取歌单 {PlaylistId} 封面失败", playlistId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IEnumerable<int> Candidates(int source) =>
        (source switch
        {
            5 => new[] { 5, 13 },
            13 => new[] { 13, 5 },
            _ => new[] { source, 5, 13 },
        }).Distinct();
}
