using System.ComponentModel;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 更新对话框的内容体。宿主见 <see cref="UpdateDialog"/>。
/// </summary>
public sealed partial class UpdatePanel : UserControl
{
    public UpdatePanel(UpdateViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelChanged;
        Notes.SetLines(ViewModel.Notes);
    }

    public UpdateViewModel ViewModel { get; }

    /// <summary>
    /// 摘掉订阅。
    /// </summary>
    /// <remarks>
    /// <b>对话框关掉时必须调。</b> ViewModel 是单例，不摘的话每开一次对话框就多挂一份面板 ——
    /// 面板会被这个单例一直按住，连它那棵可视树一起。与设置页离开时摘订阅是同一条纪律。
    /// </remarks>
    public void Detach() => ViewModel.PropertyChanged -= OnViewModelChanged;

    /// <remarks>
    /// 只认 <see cref="UpdateViewModel.Notes"/>：其余都在 XAML 里 <c>x:Bind</c> 好了，
    /// 而发布说明要走方法把行铺出来，绑不了。
    /// </remarks>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UpdateViewModel.Notes))
        {
            Notes.SetLines(ViewModel.Notes);
        }
    }
}
