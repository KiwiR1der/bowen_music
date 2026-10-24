using System.ComponentModel;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 弹出「检查更新」对话框。**挂在传入的 <see cref="XamlRoot"/> 上，不进导航栈。**
/// </summary>
/// <remarks>
/// <para>
/// <b>一次只弹一个。</b> 已经有一个对话框开着时，<see cref="ContentDialog.ShowAsync"/> 会抛 ——
/// 所以异常往上抛给调用方，由它决定怎么交代（自动检查那条路会退化成一条通知条）。
/// 这与 <c>LoginDialog</c> 把「关掉」交给 ViewModel 不同：这里没有可退的余地，
/// 弹不出来就是弹不出来。
/// </para>
/// <para>
/// <b>按钮文案由状态推出来，不是两套界面。</b> 检查中 / 已最新 / 失败 / 有新版 / 下载中 /
/// 已下好，六种状态共用这一个对话框，只有按钮与进度区在换。
/// </para>
/// <para>
/// <b>对话框关掉 = 下载停掉。</b> 这个下载没有别的露出口，关掉之后让它在后台悄悄下几十 MB
/// 是不可接受的。关到托盘不算关对话框（窗口只是隐藏），那种情况下下载继续。
/// </para>
/// </remarks>
internal static class UpdateDialog
{
    /// <summary>宽度上限。与登录框同宽（都是「一段正文 + 一排控件」的形态）。</summary>
    private const double MaxWidth = 420;

    /// <param name="checkFirst">
    /// 先查一次再显示结果（用户主动点「检查更新」）；
    /// 为假时直接渲染 ViewModel 现有的状态（自动检查已经查过了）。
    /// </param>
    public static async Task ShowAsync(
        XamlRoot xamlRoot, ElementTheme theme, UpdateViewModel viewModel, bool checkFirst = false)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);

        var panel = new UpdatePanel(viewModel);

        // 标题随状态走，由 Sync 写（每一态换一句，见 ApplyButtons）。
        // 用它而不是在内容体里自己画一行粗体：项目里每个对话框都走这个位置，
        // 字号与上边距由框架统一，自己画就对不上那一套了。
        var dialog = AppDialogs.Create(title: "检查更新", xamlRoot, theme, maxWidth: MaxWidth);
        dialog.Content = panel;

        void Sync() => ApplyButtons(dialog, viewModel);

        void OnViewModelChanged(object? sender, PropertyChangedEventArgs args) => Sync();

        void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 默认先别关：下面每种状态都只是「切到下一态」，对话框继续开着。
            args.Cancel = true;

            switch (viewModel.State)
            {
                case UpdateState.Failed:
                    _ = viewModel.CheckAsync(silent: false);
                    break;

                case UpdateState.Available when viewModel.Installer is not null:
                    _ = viewModel.StartDownloadAsync();
                    break;

                case UpdateState.Available:
                    // 便携版：打开发布页让用户自己挑。开了就没什么可留在这儿的了。
                    viewModel.OpenReleasePage();
                    args.Cancel = false;
                    break;

                case UpdateState.Downloading:
                    viewModel.CancelDownload();
                    break;

                case UpdateState.Downloaded:
                    viewModel.Install();
                    break;
            }
        }

        void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            switch (viewModel.State)
            {
                case UpdateState.Available:
                    viewModel.SkipThisVersion();
                    args.Cancel = false;
                    break;

                case UpdateState.Downloaded:
                    viewModel.OpenDownloadFolder();
                    args.Cancel = true;
                    break;

                default:
                    args.Cancel = true;
                    break;
            }
        }

        dialog.PrimaryButtonClick += OnPrimaryClick;
        dialog.SecondaryButtonClick += OnSecondaryClick;
        viewModel.PropertyChanged += OnViewModelChanged;
        Sync();

        try
        {
            var showing = dialog.ShowAsync();

            // 先让框出来（用户在「检查中」这一态看得见它在动），再放检查跑。
            if (checkFirst)
            {
                _ = viewModel.CheckAsync(silent: false);
            }

            await showing;
        }
        finally
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            dialog.PrimaryButtonClick -= OnPrimaryClick;
            dialog.SecondaryButtonClick -= OnSecondaryClick;
            panel.Detach();

            if (viewModel.State == UpdateState.Downloading)
            {
                viewModel.CancelDownload();
            }
        }
    }

    /// <summary>把当前状态翻译成标题与三颗按钮的文案。</summary>
    private static void ApplyButtons(ContentDialog dialog, UpdateViewModel viewModel)
    {
        dialog.Title = viewModel.Title;

        switch (viewModel.State)
        {
            case UpdateState.Checking:
                // 不显示一颗「检查中…」的灰按钮：内容体里那条不确定进度条已经说明在动了，
                // 再来一颗不能点的按钮只是噪音。
                Set(dialog, primary: "", secondary: "", close: "关闭");
                break;

            case UpdateState.Idle:
            case UpdateState.UpToDate:
                Set(dialog, primary: "", secondary: "", close: "关闭");
                break;

            case UpdateState.Failed:
                Set(dialog, primary: "重试", secondary: "", close: "关闭");
                break;

            case UpdateState.Available:
                // 有安装包就能应用内下；便携版没有，只能把发布页打开。
                Set(
                    dialog,
                    primary: viewModel.Installer is not null ? "下载更新" : "前往发布页面",
                    secondary: "跳过此版本",
                    close: "稍后");
                break;

            case UpdateState.Downloading:
                // 关掉按钮收起来：下载中唯一的出口是「取消下载」，关掉对话框也会取消，
                // 但那时用户以为自己在关窗口，而不是在停下载。
                Set(dialog, primary: "取消下载", secondary: "", close: "");
                break;

            case UpdateState.Downloaded:
                Set(dialog, primary: "立即安装", secondary: "打开所在文件夹", close: "关闭");
                break;
        }
    }

    private static void Set(ContentDialog dialog, string primary, string secondary, string close)
    {
        dialog.PrimaryButtonText = primary;
        dialog.SecondaryButtonText = secondary;
        dialog.CloseButtonText = close;
        dialog.DefaultButton = primary.Length > 0 ? ContentDialogButton.Primary : ContentDialogButton.Close;
    }
}
