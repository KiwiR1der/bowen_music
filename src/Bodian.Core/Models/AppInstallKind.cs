namespace Bodian.Core.Models;

/// <summary>这个进程是从哪种形态跑起来的。</summary>
public enum AppInstallKind
{
    /// <summary>NSIS 装的：应用在固定位置，有卸载项，能被安装包原地升级。</summary>
    Installed,

    /// <summary>解压出来的便携版：应用在用户自己挑的目录里，应用管不着那个目录。</summary>
    Portable,
}

/// <summary>
/// 判定当前是哪种形态。
/// </summary>
/// <remarks>
/// <para>
/// <b>判据是「程序目录里有没有 <c>Uninstall.exe</c>」。</b> 那是 NSIS 装完写进去的
/// （见 <c>installer/BowenMusic.nsi</c>），便携包里不会有。比读注册表简单，也比比对路径稳健 ——
/// 用户可能把便携版解压到 <c>%LOCALAPPDATA%\Programs\</c> 下面，路径对不上，卸载器不会凭空多出来。
/// </para>
/// <para>
/// <b>做成纯函数是为了能离线单测</b>：存在性判据由调用方注入，测试不必真去碰文件系统。
/// </para>
/// </remarks>
public static class AppInstallKindDetector
{
    /// <summary>安装版的标志文件。见 <c>installer/BowenMusic.nsi</c> 的 <c>WriteUninstaller</c>。</summary>
    public const string UninstallerFileName = "Uninstall.exe";

    /// <param name="fileExists">在程序目录里找文件的判据，通常传 <c>File.Exists</c> 包一层。</param>
    public static AppInstallKind Detect(Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        return fileExists(UninstallerFileName) ? AppInstallKind.Installed : AppInstallKind.Portable;
    }
}

/// <summary>
/// 决定新版本该下哪个包。
/// </summary>
/// <remarks>
/// <b>只有安装版才会拿到一个包。</b> 便携版返回 <c>null</c>，界面据此改成「前往发布页面」——
/// 便携版的更新是「解压覆盖掉旧目录」，而那个目录正是正在运行的程序自己所在的地方，
/// 应用做不到（文件占用、残留半新半旧的混合目录）。<c>setup.exe</c> 则能自己关掉运行中的实例、
/// 自己落到固定位置，所以只有它这条路上有「应用内下载」这回事。
/// </remarks>
public static class AppUpdateAssetPicker
{
    /// <summary>安装包文件名的结尾。按后缀匹配而不拼版本号：tag 的 <c>v</c> 前缀、第四段的有无都不影响。</summary>
    public const string InstallerSuffix = "_x64_setup.exe";

    /// <summary>挑出安装包；没有、或当前是便携版，则返回 <c>null</c>。</summary>
    public static AppUpdateAsset? Select(AppInstallKind kind, IReadOnlyList<AppUpdateAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (kind != AppInstallKind.Installed)
        {
            return null;
        }

        foreach (var asset in assets)
        {
            if (asset.Name.EndsWith(InstallerSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }
}
