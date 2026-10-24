using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IAppUpdateService" />
/// <remarks>
/// <para>
/// <b>未配置时立刻返回，不建连接、不发任何请求。</b>
/// <see cref="AppUpdateOptions.Default"/> 已指向公开仓库，所以正常构建下走的是配置好的那条路；
/// 未配置那条留着是为了让 <c>AppUpdateOptions</c> 能被替换成空的（测试与本地私有构建）。
/// </para>
/// <para>
/// <b>不打包自动更新</b>：本项目是 unpackaged，没有 MSIX 身份，装不了
/// <c>PackageManager</c> 那套。查到新版本只能自己下、由用户点「立即安装」。
/// </para>
/// <para>
/// <b>两条 HttpClient 而不是一条。</b> 检查更新用 8 秒超时（它就该快，慢等于坏了），
/// 而 <c>HttpClient.Timeout</c> 是**整个请求**的超时、含读 body —— 用它下 50 MB 的安装包
/// 会在 8 秒时被从中间切断。所以下载那条设成无限超时，靠调用方传进来的
/// <see cref="CancellationToken"/> 控制生死。两条共用同一个 <c>HttpMessageHandler</c>
/// （同一个连接池），只是超时口径不同。
/// </para>
/// </remarks>
public sealed class GitHubReleaseUpdateService : IAppUpdateService, IDisposable
{
    /// <summary>GitHub 的 API 强制要求带 User-Agent，缺了直接 403。</summary>
    private const string UserAgent = "Bowen-WinUI";

    /// <summary>检查更新的超时。见类型注释里「两条 HttpClient」那条。</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    /// <summary>下载时分块读的大小。与 <c>Stream.CopyTo</c> 的默认值一致。</summary>
    private const int CopyBufferSize = 81920;

    private readonly HttpClient _checkHttp;
    private readonly HttpClient _downloadHttp;
    private readonly ILogger _logger;
    private readonly string _downloadDirectory;

    public GitHubReleaseUpdateService(
        HttpMessageHandler handler,
        AppUpdateOptions? options = null,
        ILogger<GitHubReleaseUpdateService>? logger = null,
        string? downloadDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _checkHttp = new HttpClient(handler, disposeHandler: false) { Timeout = CheckTimeout };
        _downloadHttp = new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _logger = logger ?? NullLogger<GitHubReleaseUpdateService>.Instance;
        _downloadDirectory = downloadDirectory ?? AppPaths.UpdateDownloadDirectory;
        Options = options ?? AppUpdateOptions.Default;
    }

    public AppUpdateOptions Options { get; }

    public async Task<AppUpdateCheckResult> CheckAsync(
        Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        if (Options.ReleaseApiUri is not { } api)
        {
            return AppUpdateCheckResult.NotConfigured(
                "尚未配置更新源。这是本地私有构建，不检查更新。");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _checkHttp
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("检查更新被拒：{Status}", (int)response.StatusCode);
                return AppUpdateCheckResult.Failed($"检查失败：更新源返回 {(int)response.StatusCode}");
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return ParseLatestRelease(json, currentVersion, Options.ReleasePageUri?.AbsoluteUri ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "检查更新失败");
            return AppUpdateCheckResult.Failed("检查失败：网络不可达");
        }
    }

    public async Task<AppUpdateDownloadResult> DownloadAsync(
        AppUpdateAsset asset,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        // 文件名来自外部接口，必须只取末段 —— 一个带 ..\ 的资产名不该能写到数据目录外面去。
        var fileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrEmpty(fileName))
        {
            return AppUpdateDownloadResult.Failed("下载失败：更新源给的文件名不可用");
        }

        string target;
        var partial = "";
        try
        {
            Directory.CreateDirectory(_downloadDirectory);
            target = Path.Combine(_downloadDirectory, fileName);
            partial = target + ".part";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "下载目录不可用：{Directory}", _downloadDirectory);
            return AppUpdateDownloadResult.Failed("下载失败：无法写入下载目录");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _downloadHttp
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("下载被拒：{Status}", (int)response.StatusCode);
                return AppUpdateDownloadResult.Failed($"下载失败：更新源返回 {(int)response.StatusCode}");
            }

            // 响应头没给总长时退回发布方报的大小；两边都没有就如实报 null，不编数字。
            var total = response.Content.Headers.ContentLength;
            if (total is null or <= 0 && asset.SizeBytes > 0)
            {
                total = asset.SizeBytes;
            }

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(partial))
            {
                var buffer = new byte[CopyBufferSize];
                long received = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    // 底层流不一定理会取消令牌（内存流、已经缓冲好的响应就不理），
                    // 复制循环自己检查一次，取消才是立刻生效的、而不是等下一次真实 I/O。
                    cancellationToken.ThrowIfCancellationRequested();

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new AppUpdateProgress(received, total));
                }
            }

            if (asset.Sha256.Length > 0)
            {
                var actual = await HashFileAsync(partial, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partial, "校验不一致");
                    _logger.LogWarning("下载的包校验不一致：期望 {Expected}，实际 {Actual}", asset.Sha256, actual);
                    return AppUpdateDownloadResult.Failed("下载失败：文件校验不一致，已丢弃");
                }
            }

            // 到最后一步才改名：改名成功就意味着「这个文件是完整的」，原子性由文件系统保证。
            File.Move(partial, target, overwrite: true);

            _logger.LogInformation("更新包已下好：{Path}", target);
            return AppUpdateDownloadResult.Completed(target);
        }
        catch (OperationCanceledException)
        {
            // 取消不是错误。半截文件必须删掉，否则下次会以为「已经下好了」。
            TryDelete(partial, "用户取消");
            return AppUpdateDownloadResult.Cancelled();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "下载更新包失败");
            TryDelete(partial, "下载失败");
            return AppUpdateDownloadResult.Failed("下载失败：网络或磁盘出错");
        }
    }

    public void Dispose()
    {
        _checkHttp.Dispose();
        _downloadHttp.Dispose();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private void TryDelete(string path, string reason)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 删不掉不该让整件事失败：下次下载会直接覆盖它。留一条日志便于排查。
            _logger.LogDebug(exception, "半截文件删不掉（{Reason}）：{Path}", reason, path);
        }
    }

    /// <summary>
    /// 解析 GitHub 的 <c>releases/latest</c> 响应。
    /// </summary>
    /// <remarks>
    /// <b>抽成静态纯函数是为了能离线单测</b>：喂一段 fixture JSON 就能覆盖
    /// 「tag 带不带 v」「版本相同 / 更新 / 更旧」「字段缺失」「assets 为空」这几条，
    /// 不必真的联网、也不必造一个 GitHub 账号。
    /// </remarks>
    /// <param name="json">响应体。</param>
    /// <param name="current">当前版本。</param>
    /// <param name="fallbackReleasePageUrl">响应里没有 <c>html_url</c> 时用哪个地址。</param>
    public static AppUpdateCheckResult ParseLatestRelease(
        string json, Version current, string fallbackReleasePageUrl)
    {
        ArgumentNullException.ThrowIfNull(current);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return AppUpdateCheckResult.Failed("检查失败：更新源返回的内容看不懂");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tagElement)
                || tagElement.GetString() is not { Length: > 0 } tag)
            {
                return AppUpdateCheckResult.Failed("检查失败：更新源返回的内容看不懂");
            }

            // tag 常见写 `v0.2.0`，也可能不带前缀。
            if (!Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var latest))
            {
                return AppUpdateCheckResult.Failed($"检查失败：更新源的版本号无法解析（{tag}）");
            }

            if (latest <= current)
            {
                return AppUpdateCheckResult.UpToDate(current);
            }

            var url = root.TryGetProperty("html_url", out var urlElement)
                && urlElement.GetString() is { Length: > 0 } html
                    ? html
                    : fallbackReleasePageUrl;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var releasePage))
            {
                return AppUpdateCheckResult.Failed("发现新版本，但更新源没有给出可用的发布页地址");
            }

            var body = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? ""
                : "";

            return AppUpdateCheckResult.Available(
                latest,
                releasePage,
                $"发现新版本 {AppUpdateCheckResult.Format(latest)}",
                body,
                ParseAssets(root, body));
        }
    }

    /// <summary>把响应里的 <c>assets[]</c> 摘出来。</summary>
    /// <remarks>
    /// <para>
    /// <b>校验和优先取 <c>digest</c> 字段</b>（GitHub 现在直接给 <c>sha256:…</c>），
    /// 拿不到再退回正文里那张校验表 —— 两者实测逐字一致，但正文是我们自己在
    /// <c>changes.md</c> 里控制的，比接口字段更不容易因为平台改动而消失。
    /// </para>
    /// <para>
    /// 单个资产缺字段就跳过它，不让一个坏条目把整次检查拖垮：剩下的资产仍然可用。
    /// </para>
    /// </remarks>
    private static List<AppUpdateAsset> ParseAssets(JsonElement root, string body)
    {
        var assets = new List<AppUpdateAsset>();

        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }

        var fromBody = ParseChecksumTable(body);

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !TryGetString(element, "name", out var name)
                || !TryGetString(element, "browser_download_url", out var url)
                || !Uri.TryCreate(url, UriKind.Absolute, out var downloadUri))
            {
                continue;
            }

            var size = element.TryGetProperty("size", out var sizeElement)
                && sizeElement.TryGetInt64(out var value)
                    ? value
                    : 0;

            var sha = ParseDigest(element);
            if (sha.Length == 0 && fromBody.TryGetValue(name, out var fallback))
            {
                sha = fallback;
            }

            assets.Add(new AppUpdateAsset(name, size, downloadUri, sha));
        }

        return assets;
    }

    /// <summary><c>"sha256:abcd…"</c> → <c>"abcd…"</c>。不是 sha256 或字段缺失都返回空串。</summary>
    private static string ParseDigest(JsonElement element)
    {
        const string prefix = "sha256:";

        if (!TryGetString(element, "digest", out var digest)
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var value = digest[prefix.Length..].Trim();
        return IsHex64(value) ? value.ToLowerInvariant() : "";
    }

    /// <summary>从发布正文里那张 <c>| 文件 | SHA-256 |</c> 表格读出「文件名 → 哈希」。</summary>
    /// <remarks>
    /// 手写切分而不是正则：<c>Core</c> 是 AOT 友好的，正则要么走源生成要么拖一份解释器进来，
    /// 而这里的格式是自己定的、只有一种写法。
    /// </remarks>
    private static Dictionary<string, string> ParseChecksumTable(string body)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(body))
        {
            return table;
        }

        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < 4)
            {
                continue;
            }

            var name = cells[1].Trim().Trim('`').Trim();
            var hash = cells[2].Trim().Trim('`').Trim();

            // 表头（`文件` / `SHA-256`）与分隔行（`---`）到不了这一步。
            if (name.Length > 0 && IsHex64(hash))
            {
                table[name] = hash.ToLowerInvariant();
            }
        }

        return table;
    }

    private static bool IsHex64(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = "";

        if (!element.TryGetProperty(property, out var found)
            || found.ValueKind != JsonValueKind.String
            || found.GetString() is not { Length: > 0 } text)
        {
            return false;
        }

        value = text;
        return true;
    }
}
