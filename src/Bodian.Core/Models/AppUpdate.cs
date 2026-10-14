namespace Bodian.Core.Models;

/// <summary>一次「检查更新」的结果状态。</summary>
public enum AppUpdateStatus
{
    /// <summary>没配更新源（本地私有构建、测试）。见 <see cref="AppUpdateOptions"/>。</summary>
    NotConfigured,

    /// <summary>已经是最新的。</summary>
    UpToDate,

    /// <summary>有新版本。</summary>
    UpdateAvailable,

    /// <summary>检查失败（断网、接口变了）。</summary>
    Failed,
}

/// <summary>检查更新的结果。</summary>
/// <param name="Status">状态。</param>
/// <param name="LatestVersion">查到的最新版本。没查到就是 <c>null</c>。</param>
/// <param name="ReleasePageUri">发布页面。浏览器那条路（便携版）与「查看完整说明」用它。</param>
/// <param name="Message">给用户看的一句话。<b>永远是人话</b>，不是异常串。</param>
public sealed record AppUpdateCheckResult(
    AppUpdateStatus Status,
    Version? LatestVersion = null,
    Uri? ReleasePageUri = null,
    string Message = "")
{
    /// <summary>
    /// 这一版的发布说明（<c>changes.md</c> 里那段正文的原文）。
    /// 渲染走 <see cref="ReleaseNotes.Parse"/>。
    /// </summary>
    public string ReleaseNotes { get; init; } = "";

    /// <summary>
    /// Release 上挂的全部产物。
    /// </summary>
    /// <remarks>
    /// <b>这里给全量，挑哪个是界面的事</b>（见 <see cref="AppUpdateAssetPicker"/>）：
    /// 服务端的职责是「说了什么」，下哪个包取决于本机是安装版还是便携版。
    /// </remarks>
    public IReadOnlyList<AppUpdateAsset> Assets { get; init; } = [];

    public static AppUpdateCheckResult NotConfigured(string message) =>
        new(AppUpdateStatus.NotConfigured, Message: message);

    public static AppUpdateCheckResult UpToDate(Version current) =>
        new(AppUpdateStatus.UpToDate, current, Message: $"已是最新版本（{Format(current)}）");

    public static AppUpdateCheckResult Available(
        Version latest,
        Uri releasePage,
        string message,
        string releaseNotes = "",
        IReadOnlyList<AppUpdateAsset>? assets = null) =>
        new(AppUpdateStatus.UpdateAvailable, latest, releasePage, message)
        {
            ReleaseNotes = releaseNotes,
            Assets = assets ?? [],
        };

    public static AppUpdateCheckResult Failed(string message) =>
        new(AppUpdateStatus.Failed, Message: message);

    /// <summary>版本号的显示形式：只到 <c>Major.Minor.Build</c>，不显示第四段。</summary>
    public static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
}
