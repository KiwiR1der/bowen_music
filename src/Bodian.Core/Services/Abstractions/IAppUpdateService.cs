using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 检查更新与下载安装包。
/// </summary>
/// <remarks>
/// <para>
/// <b>本接口不做任何自主行为</b>：不自己起定时器、不后台轮询、不在构造时联网。
/// 启动时查一次这件事由界面层决定（默认开，可在设置里关掉），见 <c>docs/update.md</c>。
/// </para>
/// <para>
/// <b>不含遥测的含义</b>：这里出站的两个请求都只到 GitHub —— 检查更新拉一次
/// <c>releases/latest</c>，下载拉一次资产直链。全程不带设备标识、不带账号、不携带本地状态。
/// </para>
/// </remarks>
public interface IAppUpdateService
{
    /// <summary>当前配置。界面据此决定「项目主页」按钮是否可用。</summary>
    AppUpdateOptions Options { get; }

    /// <summary>
    /// 查一次最新版本。
    /// </summary>
    /// <remarks>
    /// <b>永不抛。</b> 失败一律返回 <see cref="AppUpdateStatus.Failed"/> 且带一句人话，
    /// 界面直接显示，不需要再包一层 try。
    /// </remarks>
    Task<AppUpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载一个产物。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>永不抛，取消也算正常返回</b>（<see cref="AppUpdateDownloadStatus.Cancelled"/>）——
    /// 「用户点了取消」与「下载失败」在界面上是两件完全不同的事，调用方不该靠捕获异常去分辨。
    /// </para>
    /// <para>
    /// <b>先写 <c>.part</c> 再改名。</b> 中途取消或失败会删掉半截文件，用户永远不会看到
    /// 一个「看起来下好了、其实是残缺的」安装包。
    /// </para>
    /// <para>
    /// <b>进度报告可能很密</b>（每 80 KB 一次，50 MB 的包约 640 次）。界面若要按百分比
    /// 去重再刷新，那是界面的事 —— 这里如实报告，不做节流，免得把「真实进度」这件事
    /// 塞进一个传输层不该管的口径。
    /// </para>
    /// </remarks>
    /// <param name="asset">下哪一个。由 <see cref="AppUpdateAssetPicker"/> 从检查结果里挑。</param>
    /// <param name="progress">进度回调。不关心就传 <c>null</c>。</param>
    Task<AppUpdateDownloadResult> DownloadAsync(
        AppUpdateAsset asset,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
