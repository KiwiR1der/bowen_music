using Bowen.Core.Models;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Bowen.WinUI.Controls;

public sealed partial class SongCommentsPanel : UserControl
{
    public SongCommentsPanel(SongCommentsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SongCommentsViewModel ViewModel { get; }
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 把焦点移到关闭按钮。
    /// </summary>
    /// <remarks>
    /// <b>默认 Programmatic</b>：焦点框是给键盘用户的，鼠标点击之后也用 Keyboard 移焦点，
    /// 关闭按钮上会留下一圈白边，看着像选中态没清掉。键盘路径由调用方显式传 Keyboard。
    /// </remarks>
    public void FocusCloseButton(FocusState focusState = FocusState.Programmatic)
        => CloseButton.Focus(focusState);
    private void OnCloseClick(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 把「正文是否被行数上限截断」从模板回写到行上，供「更多」按钮决定是否露面。
    /// </summary>
    /// <remarks>
    /// 只在 <c>TextBlock</c> 自己量完之后才知道结果，所以挂 SizeChanged 而不是绑属性。
    /// 容器回收复用时若新旧内容尺寸恰好一致、这个事件不触发，行上留的会是上一次的值 ——
    /// 那种情况下「更多」的显隐可能与内容不符，点开一次就会借布局刷新纠正。
    /// </remarks>
    private void OnCommentContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is TextBlock { DataContext: SongCommentRow row } block) row.IsContentTrimmed = block.IsTextTrimmed;
    }

    private async void OnRecommendedClick(object sender, RoutedEventArgs args)
    {
        // ToggleButton 会自行反转 IsChecked；同一排序再点一次时保持选中。
        RecommendedButton.IsChecked = true;
        LatestButton.IsChecked = false;
        await ViewModel.ChangeSortAsync(SongCommentSort.Recommended);
        ScrollToTop();
    }

    private async void OnLatestClick(object sender, RoutedEventArgs args)
    {
        LatestButton.IsChecked = true;
        RecommendedButton.IsChecked = false;
        await ViewModel.ChangeSortAsync(SongCommentSort.Latest);
        ScrollToTop();
    }

    private async void OnEditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
        {
            args.Handled = true;
            if (ViewModel.PublishCommand.CanExecute(null)) await ViewModel.PublishCommand.ExecuteAsync(null);
        }
    }

    private void ScrollToTop()
    {
        if (ViewModel.Items.Count > 0) CommentsList.ScrollIntoView(ViewModel.Items[0]);
    }
}
