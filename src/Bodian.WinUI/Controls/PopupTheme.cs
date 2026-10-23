using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 让弹层跟随应用主题，而不是继承它们挂靠的固定深色沉浸页。
/// </summary>
/// <remarks>
/// <para>
/// 歌词页与 MV 页都是 <c>RequestedTheme="Dark"</c>，Flyout / Popup 会从放置目标继承 Dark，
/// 于是应用切到浅色时，菜单和音量条仍是一块深色。
/// </para>
/// <para>
/// <b>主题要设在承载弹层的那一层</b>：Flyout 是 <see cref="FlyoutPresenter"/>（只改内容控件
/// 不会影响它自己那层背景），普通 <c>Popup</c> 则是它的内容根。弹层是 popup，
/// 头一次打开时才会建出来，所以调用方要在打开时（<c>Opening</c> / <c>Opened</c>）再设一次。
/// </para>
/// </remarks>
internal static class PopupTheme
{
    /// <summary>
    /// 应用当前的清晰档位：浅色 / 深色；跟随系统时是 <see cref="ElementTheme.Default"/>。
    /// </summary>
    /// <remarks>
    /// 来自 App 资源里的主题单例 —— 给拿不到主窗口的 XAML 控件（音量按钮）用。
    /// </remarks>
    public static ElementTheme CurrentAppTheme =>
        Application.Current.Resources["BodianTheme"] is ThemeViewModel theme
            ? theme.RequestedTheme
            : ElementTheme.Default;

    /// <summary>
    /// 把主题设到弹层内容与承载它的 <see cref="FlyoutPresenter"/> 上。
    /// </summary>
    /// <remarks>
    /// 找不到 presenter 时只设内容本身：普通 Popup 走的正是这一条，
    /// 文字与控件颜色仍然正确，只是外面那层底色由调用方自己的内容根负责。
    /// </remarks>
    public static void Apply(FrameworkElement content, ElementTheme theme)
    {
        content.RequestedTheme = theme;

        var node = (DependencyObject?)content;
        while (node is not null && node is not FlyoutPresenter)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is FlyoutPresenter presenter)
        {
            presenter.RequestedTheme = theme;
        }
    }
}
