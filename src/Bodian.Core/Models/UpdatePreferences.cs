namespace Bodian.Core.Models;

/// <summary>
/// 更新偏好（明文 JSON）。
/// </summary>
/// <param name="AutoCheck">
/// 启动时自动检查。<b>默认开</b>：默认关掉的「检查更新」等于没有，
/// 而这个动作只是一次 GET，不涉及账号、也不上报任何东西。
/// </param>
/// <param name="SkippedVersion">
/// 用户点过「跳过此版本」的那个版本号（如 <c>0.2.0</c>）。空串 = 没跳过任何版本。
/// </param>
public sealed record UpdatePreferences(bool AutoCheck = true, string SkippedVersion = "")
{
    public static UpdatePreferences Default { get; } = new();

    /// <summary>
    /// 这个版本是不是用户已经表示过不想被提示了。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只对自动检查生效</b>：用户主动点「检查更新」时不给结果才是奇怪的，
    /// 所以手动那条路不看这个判据。
    /// </para>
    /// <para>
    /// <b>比的是「小于等于」不是「等于」</b>：跳过的版本号解析不出来（用户手改过文件、
    /// 或格式变了）时当作没跳过，宁可多提示一次，也不要因为一个坏字符串就永远静默。
    /// </para>
    /// </remarks>
    public bool IsSkipped(Version latest)
    {
        ArgumentNullException.ThrowIfNull(latest);

        return Version.TryParse(SkippedVersion.Trim(), out var skipped) && latest <= skipped;
    }
}
