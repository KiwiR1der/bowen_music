namespace Bodian.Core.Models;

/// <summary>一次下载的结果状态。</summary>
public enum AppUpdateDownloadStatus
{
    /// <summary>下完了，并且（拿得到校验和时）校验通过。</summary>
    Completed,

    /// <summary>用户取消，或应用退出。**不是错误**，界面不该报错。</summary>
    Cancelled,

    /// <summary>网络、磁盘、校验失败。</summary>
    Failed,
}

/// <summary>
/// 下载进度。
/// </summary>
/// <param name="ReceivedBytes">已收到的字节数。</param>
/// <param name="TotalBytes">
/// 总字节数。发布方与响应头都没给就是 <c>null</c> —— 界面此时用不确定进度条，不要编一个数字出来。
/// </param>
public sealed record AppUpdateProgress(long ReceivedBytes, long? TotalBytes)
{
    /// <summary>完成比例（0–1）。拿不到总长时是 <c>null</c>。</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0d, 1d) : null;
}

/// <summary>一次下载的结果。</summary>
/// <param name="Status">状态。</param>
/// <param name="FilePath">下好的文件在哪。<b>只有成功时非空。</b></param>
/// <param name="Message">给用户看的一句话。<b>永远是人话</b>，不是异常串。</param>
public sealed record AppUpdateDownloadResult(
    AppUpdateDownloadStatus Status,
    string? FilePath = null,
    string Message = "")
{
    public static AppUpdateDownloadResult Completed(string filePath) =>
        new(AppUpdateDownloadStatus.Completed, filePath, "下载完成");

    public static AppUpdateDownloadResult Cancelled() =>
        new(AppUpdateDownloadStatus.Cancelled, Message: "已取消下载");

    public static AppUpdateDownloadResult Failed(string message) =>
        new(AppUpdateDownloadStatus.Failed, Message: message);
}
