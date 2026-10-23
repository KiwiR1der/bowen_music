namespace Bodian.WinUI.Services;

/// <summary>
/// 提供主窗口的句柄。
/// </summary>
/// <remarks>
/// <para>
/// WinUI 3 桌面端的 <see cref="Windows.Storage.Pickers.FileOpenPicker"/> 之类
/// 需要在显示前绑定宿主窗口（<c>WinRT.Interop.InitializeWithWindow.Initialize</c>），
/// 否则一打开就抛 —— 而句柄只有 <c>MainWindow</c> 拿得到。
/// </para>
/// <para>
/// 与 <see cref="IPlaylistLibrarySink"/> 同一种接线：外壳实现并注册成单例。
/// </para>
/// </remarks>
public interface IWindowHandleProvider
{
    /// <summary>主窗口的原生句柄。</summary>
    nint WindowHandle { get; }

    /// <summary>
    /// 把主窗口带回前台。
    /// </summary>
    /// <remarks>
    /// <b>不能用 <c>SetForegroundWindow</c> 代替。</b> 主窗口在「关闭到托盘」时是
    /// <c>AppWindow.Hide()</c>，既不是最小化也不可见；只调 <c>SetForegroundWindow</c>
    /// 不会把它显示出来。这里统一走主窗口自己的显示路径（<c>AppWindow.Show</c>、
    /// 还原最小化、激活），小窗的「回到主界面」与托盘入口因此共用同一套行为。
    /// </remarks>
    void ShowMainWindow();
}
