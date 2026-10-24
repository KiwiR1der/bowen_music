namespace Bowen.Core.Models;

/// <summary>
/// Release 上的一个可下载产物。
/// </summary>
/// <param name="Name">文件名，如 <c>BowenMusic_0.2.0_x64_setup.exe</c>。</param>
/// <param name="SizeBytes">字节数。发布方没给就是 <c>0</c>，显示时按「未知」处理。</param>
/// <param name="DownloadUri">直链（<c>browser_download_url</c>）。</param>
/// <param name="Sha256">
/// 小写十六进制的 SHA-256。拿不到就是空串 —— 那时下载照常进行，只是不校验。
/// </param>
public sealed record AppUpdateAsset(string Name, long SizeBytes, Uri DownloadUri, string Sha256 = "");
