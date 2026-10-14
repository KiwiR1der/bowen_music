using System.Diagnostics;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>「检查更新」这件事当前处在哪一步。对话框的按钮与内容全部由它推出来。</summary>
public enum UpdateState
{
    /// <summary>还没查过。</summary>
    Idle,

    /// <summary>正在查。</summary>
    Checking,

    /// <summary>已经是最新的。</summary>
    UpToDate,

    /// <summary>有新版本，等用户决定。</summary>
    Available,

    /// <summary>正在下载安装包。</summary>
    Downloading,

    /// <summary>下好了，可以装了。</summary>
    Downloaded,

    /// <summary>最近一次检查失败了。</summary>
    Failed,
}

/// <summary>
/// 检查更新与安装。**单例**。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态全在这里，对话框只负责渲染。</b> 「关于」页那颗按钮、启动时的自动检查、
/// 下载进度与取消，走的都是这一个实例 —— 与设置页「ViewModel 持真实状态、页面只绑」同源。
/// </para>
/// <para>
/// <b>只对外发事件，不反向依赖外壳。</b> 查到新版本要弹框、装之前要退出应用，
/// 这两件事都要外壳的 <c>XamlRoot</c> 与它那条既有的退出链，而外壳本身又需要注入本类 ——
/// 直接互相注入会成环。所以这里是 <see cref="UpdateFound"/> 与 <see cref="ExitRequested"/>，
/// 由外壳订阅（同 <c>LoginViewModel.LoggedIn</c> 的写法）。
/// </para>
/// <para>
/// <b>全部方法都在 UI 线程上被调用</b>（界面事件、或外壳的启动钩子），
/// 所以状态字段与计时器都不加锁。
/// </para>
/// </remarks>
public sealed partial class UpdateViewModel : ObservableObject
{
    /// <summary>
    /// 启动后等多久才去查。
    /// </summary>
    /// <remarks>
    /// <b>不早于首屏。</b> 启动瞬间就弹一个模态框是最糟的形态，而这个等待不花任何代价 ——
    /// 检查本身是后台的，晚几秒知道结果不影响任何事。
    /// </remarks>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    private readonly IAppUpdateService _updates;
    private readonly IUpdatePreferencesStore _preferences;
    private readonly ILogger _logger;

    private CancellationTokenSource? _downloadCancellation;
    private UpdatePreferences _saved;

    /// <summary>构造函数还在填初始值。填的时候不落盘 —— 否则每次启动都会白写一次文件。</summary>
    private bool _loaded;

    /// <summary>上一次报给界面的整百分点。用来压掉每 80 KB 一次的重复刷新。</summary>
    private double _reportedPercent = -1;

    private bool _startupCheckStarted;

    public UpdateViewModel(
        IAppUpdateService updates,
        IUpdatePreferencesStore preferences,
        ILogger<UpdateViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(preferences);

        _updates = updates;
        _preferences = preferences;
        _logger = logger ?? NullLogger<UpdateViewModel>.Instance;

        // 版本号从程序集读，与「关于」页同一个来源（src/Directory.Build.props 的 <Version>）。
        var version = typeof(UpdateViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
        CurrentVersion = version;
        CurrentVersionText = AppUpdateCheckResult.Format(version);

        _saved = preferences.Load();
        AutoCheck = _saved.AutoCheck;
        SkippedVersion = _saved.SkippedVersion;
        _loaded = true;
    }

    /// <summary>自动检查查到了新版本、且这次不该被跳过。外壳据此弹对话框。</summary>
    public event EventHandler? UpdateFound;

    /// <summary>安装程序已经起来了。外壳据此走它自己那条退出链。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>当前版本，如 <c>0.1.0</c>。</summary>
    public Version CurrentVersion { get; }

    public string CurrentVersionText { get; }

    /// <summary>带 <c>v</c> 前缀，给界面显示用。</summary>
    public string CurrentVersionLabel => $"v{CurrentVersionText}";

    /// <summary>
    /// 启动时自动检查。<b>默认开</b>，改了立刻落盘。
    /// </summary>
    /// <remarks>
    /// 自动检查只做「查」和「不查」这一个开关，没有「只检查不下载」之类的细分 ——
    /// 下载永远要用户点一下，不存在需要单独关掉的东西。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoCheckDescription))]
    public partial bool AutoCheck { get; set; }

    /// <summary>用户点过「跳过此版本」的那个版本号。空串 = 没跳过。</summary>
    public string SkippedVersion { get; private set; }

    /// <summary>开关那一行的说明，随状态变。</summary>
    public string AutoCheckDescription => AutoCheck
        ? "启动后自动检查，有新版本时提示一次"
        : "已关闭，只能手动检查";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(CanStartCheck))]
    public partial UpdateState State { get; set; }

    /// <summary>
    /// 「检查更新」那颗按钮能不能点。
    /// </summary>
    /// <remarks>
    /// 正在查、正在下载时都禁掉：前者的第二次点击会被 <see cref="CheckAsync"/> 直接丢掉
    /// （看起来像没反应），后者再弹一次对话框会与正在跑的那次撞上。
    /// </remarks>
    public bool CanStartCheck => State is not (UpdateState.Checking or UpdateState.Downloading);

    /// <summary>
    /// 对话框的标题，随状态换一句。
    /// </summary>
    /// <remarks>
    /// 写进 <c>ContentDialog.Title</c>，不在内容体里自己画 —— 项目里每个对话框都走那个位置，
    /// 字号与上边距由框架统一，自己画一行 20px 粗体只会让它与其它对话框对不上。
    /// </remarks>
    [ObservableProperty]
    public partial string Title { get; set; } = "检查更新";

    /// <summary>标题下面那一行小字：当前版本、安装包大小、或失败原因。</summary>
    [ObservableProperty]
    public partial string DetailText { get; set; } = "";

    /// <summary>这一版的发布说明，已切好。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReleaseNoteLine> Notes { get; set; } = [];

    /// <summary>下载进度、取消、失败这些临时状态。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = "";

    /// <summary>
    /// 进度槽。**检查中与下载中共用**，分两处画会让对话框在「查完 → 开始下载」时跳一下高度。
    /// </summary>
    [ObservableProperty]
    public partial bool IsProgressVisible { get; set; }

    /// <summary>检查中、或下载时拿不到总长 —— 都转不确定进度条。</summary>
    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    /// <summary>查到的新版本。没查到就是 <c>null</c>。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(LatestVersionLabel))]
    public partial Version? LatestVersion { get; set; }

    /// <summary>新版本的显示形式，如 <c>v0.2.0</c>。没查到就是空串。</summary>
    public string LatestVersionLabel =>
        LatestVersion is { } latest ? $"v{AppUpdateCheckResult.Format(latest)}" : "";

    /// <summary>这一版该下的安装包。<b>便携版恒为 <c>null</c></b> —— 那一路只引导去发布页。</summary>
    public AppUpdateAsset? Installer { get; private set; }

    /// <summary>发布页面。便携版的「前往发布页面」与「查看完整说明」用它。</summary>
    public Uri? ReleasePageUri { get; private set; }

    /// <summary>下好的安装包在哪。</summary>
    public string? DownloadedFilePath { get; private set; }

    public bool IsChecking => State == UpdateState.Checking;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>查到了新版本（不论用户跳没跳过）。「关于」页据此挂一句提示。</summary>
    public bool HasUpdate => LatestVersion is not null && LatestVersion > CurrentVersion;

    /// <summary>
    /// 启动时查一次。**整个进程只跑一次**，且只在开关打开时真的去查。
    /// </summary>
    public async Task RunStartupCheckAsync()
    {
        if (_startupCheckStarted || !AutoCheck)
        {
            return;
        }

        _startupCheckStarted = true;

        try
        {
            await Task.Delay(StartupDelay).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (await CheckAsync(silent: true).ConfigureAwait(true))
        {
            UpdateFound?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 查一次。
    /// </summary>
    /// <param name="silent">
    /// 自动检查。<b>为真时失败不打扰用户</b>（只在日志里留痕），
    /// 且已跳过的版本不返回「该弹框」。
    /// </param>
    /// <returns>是不是该弹对话框告诉用户有新版本。</returns>
    public async Task<bool> CheckAsync(bool silent, CancellationToken cancellationToken = default)
    {
        if (State == UpdateState.Checking)
        {
            return false;
        }

        State = UpdateState.Checking;
        Title = "正在检查更新";
        DetailText = $"当前 {CurrentVersionLabel}";
        StatusText = "";
        Notes = [];

        // 检查与下载共用进度槽，检查期间是不确定态（见 UpdatePanel.xaml 的说明）。
        IsProgressVisible = true;
        IsProgressIndeterminate = true;

        var result = await _updates.CheckAsync(CurrentVersion, cancellationToken).ConfigureAwait(true);

        IsProgressVisible = false;
        IsProgressIndeterminate = false;

        switch (result.Status)
        {
            case AppUpdateStatus.UpdateAvailable:
                LatestVersion = result.LatestVersion;
                ReleasePageUri = result.ReleasePageUri;
                Notes = ReleaseNotes.Parse(result.ReleaseNotes);

                // 便携版拿不到安装包：它的更新是「解压覆盖掉自己所在的目录」，应用做不到。
                Installer = AppUpdateAssetPicker.Select(DetectInstallKind(), result.Assets);

                Title = $"发现新版本 v{AppUpdateCheckResult.Format(result.LatestVersion!)}";
                DetailText = DetailFor(Installer, ReleasePageUri, CurrentVersionLabel);
                State = UpdateState.Available;

                // 跳过判据只挡自动检查 —— 用户主动点的那一次，就该给结果。
                return !(silent && _saved.IsSkipped(result.LatestVersion!));

            case AppUpdateStatus.UpToDate:
                Title = "已是最新版本";
                DetailText = result.Message;
                State = UpdateState.UpToDate;
                return false;

            default:
                // 自动检查失败静默：断网、接口变了都不是用户做错的事，不值得给一个模态框。
                _logger.LogInformation("检查更新未成功：{Status} {Message}", result.Status, result.Message);

                if (silent)
                {
                    State = UpdateState.Idle;
                    return false;
                }

                Title = "检查更新失败";
                DetailText = result.Message;
                State = UpdateState.Failed;
                return false;
        }
    }

    /// <summary>下安装包。可被 <see cref="CancelDownload"/> 叫停。</summary>
    public async Task StartDownloadAsync()
    {
        if (Installer is not { } asset || State == UpdateState.Downloading)
        {
            return;
        }

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        _reportedPercent = -1;

        State = UpdateState.Downloading;
        Title = "正在下载更新";
        DetailText = asset.Name;
        StatusText = "";
        ProgressPercent = 0;
        IsProgressIndeterminate = false;
        IsProgressVisible = true;

        var progress = new Progress<AppUpdateProgress>(OnProgress);
        var result = await _updates
            .DownloadAsync(asset, progress, _downloadCancellation.Token)
            .ConfigureAwait(true);

        IsProgressVisible = false;

        switch (result.Status)
        {
            case AppUpdateDownloadStatus.Completed:
                DownloadedFilePath = result.FilePath;
                State = UpdateState.Downloaded;
                Title = "下载完成";
                DetailText = "点「立即安装」会关闭本应用并启动安装程序。";
                StatusText = result.FilePath ?? "";
                break;

            case AppUpdateDownloadStatus.Cancelled:
                State = UpdateState.Available;
                Title = $"发现新版本 v{AppUpdateCheckResult.Format(LatestVersion!)}";
                StatusText = result.Message;
                break;

            default:
                State = UpdateState.Available;
                Title = $"发现新版本 v{AppUpdateCheckResult.Format(LatestVersion!)}";
                StatusText = result.Message;
                break;
        }
    }

    /// <summary>
    /// 叫停下到一半的下载。**不是错误**：界面回到「可以下载」那一态，不弹报错。
    /// </summary>
    /// <remarks>
    /// 真退出前由外壳调一次（见 <c>App.xaml.cs</c> 的 <c>window.Closed</c>）。
    /// <b>关到托盘不调</b> —— 那是隐藏，用户还在用，把下载掐掉是错的。
    /// </remarks>
    public void CancelDownload() => _downloadCancellation?.Cancel();

    /// <summary>记下「这一版别再提示了」。</summary>
    public void SkipThisVersion()
    {
        if (LatestVersion is not { } latest)
        {
            return;
        }

        SkippedVersion = AppUpdateCheckResult.Format(latest);
        Persist(_saved with { SkippedVersion = SkippedVersion });
    }

    /// <summary>把下好的安装程序跑起来，然后请外壳退出。</summary>
    public void Install()
    {
        if (DownloadedFilePath is not { } path || !File.Exists(path))
        {
            StatusText = "安装包不在了，请重新下载";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "启动安装程序失败：{Path}", path);
            StatusText = $"启动安装程序失败：{exception.Message}";
            return;
        }

        // 安装器自己也会检测并关掉运行中的实例（见 installer/BowenMusic.nsi），
        // 但我们先退更干净：由外壳走它那条既有的收尾链，而不是让安装器来杀进程。
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>打开下载所在目录。没下过就打开数据目录下的 updates。</summary>
    public void OpenDownloadFolder()
    {
        var path = DownloadedFilePath is { } file ? Path.GetDirectoryName(file) : null;
        Open(path is { Length: > 0 } ? path : Core.Services.AppPaths.UpdateDownloadDirectory);
    }

    /// <summary>打开发布页面。</summary>
    public void OpenReleasePage() => Open(ReleasePageUri?.AbsoluteUri ?? _updates.Options.ReleasePageUri?.AbsoluteUri);

    partial void OnAutoCheckChanged(bool value)
    {
        // 构造函数填初值那一次不落盘。见 _loaded 的说明。
        if (_loaded)
        {
            Persist(_saved with { AutoCheck = value });
        }
    }

    private void Persist(UpdatePreferences next)
    {
        _saved = next;
        _preferences.Save(next);
    }

    /// <summary>
    /// 进度回调。**按整百分点去重**：一次 50 MB 的下载会来六百多次回调，
    /// 每次都刷界面纯属浪费（进度条自己会插值，看不出差别）。
    /// </summary>
    private void OnProgress(AppUpdateProgress report)
    {
        if (report.Fraction is not { } fraction)
        {
            // 两边都没给总长：转不确定进度条，不编一个假数字出来。
            IsProgressIndeterminate = true;
            DetailText = $"已下载 {ByteSizeLabel.Format(report.ReceivedBytes)}";
            return;
        }

        var percent = Math.Floor(fraction * 100);
        if (IsProgressIndeterminate)
        {
            IsProgressIndeterminate = false;
        }

        if (percent <= _reportedPercent)
        {
            return;
        }

        _reportedPercent = percent;
        ProgressPercent = percent;
        DetailText = $"{ByteSizeLabel.Format(report.ReceivedBytes)} / {ByteSizeLabel.Format(report.TotalBytes!.Value)}";
    }

    /// <summary>当前进程是从哪种形态跑起来的。判据见 <see cref="AppInstallKindDetector"/>。</summary>
    private static AppInstallKind DetectInstallKind() =>
        AppInstallKindDetector.Detect(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name)));

    private static string DetailFor(AppUpdateAsset? installer, Uri? releasePage, string currentVersion)
    {
        if (installer is not null)
        {
            var size = installer.SizeBytes > 0 ? $" · 安装包 {ByteSizeLabel.Format(installer.SizeBytes)}" : "";
            return $"当前 {currentVersion}{size}";
        }

        return releasePage is not null
            ? $"当前 {currentVersion} · 便携版需自行下载解压，将打开发布页面"
            : $"当前 {currentVersion} · 更新源没有给出下载地址，将打开发布页面";
    }

    /// <remarks>
    /// 用 <c>Process.Start</c> + <c>UseShellExecute</c> 而不是 <c>Launcher.LaunchUriAsync</c>：
    /// 未打包应用没有 MSIX 身份，后者在部分环境里会静默失败。与「关于」页那两处同一个取舍。
    /// </remarks>
    private void Open(string? target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开失败：{Target}", target);
        }
    }
}
