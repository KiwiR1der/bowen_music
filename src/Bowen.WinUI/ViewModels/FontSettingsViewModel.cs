using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.Core.Services.Implementations;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bowen.WinUI.ViewModels;

/// <summary>界面字体偏好。设置只落盘，应用重启后统一应用。</summary>
public sealed partial class FontSettingsViewModel : ObservableObject
{
    private readonly IFontSettingsStore _store;

    public FontSettingsViewModel(IFontSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        CurrentFontFamily = store.Load().Normalized().FontFamily;

        // 在 DI 首次解析本服务时执行；组合根保证它发生在主窗口创建之前。
        if (!string.IsNullOrEmpty(CurrentFontFamily))
            Application.Current.Resources["ContentControlThemeFontFamily"] = new FontFamily(CurrentFontFamily);

        var installed = CanvasTextFormat.GetSystemFontFamilies()
            .Where(name => !string.IsNullOrWhiteSpace(name));
        if (!string.IsNullOrEmpty(CurrentFontFamily)) installed = installed.Append(CurrentFontFamily);
        FontNames = [.. installed
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>已保存的字体族名；空串 = 跟随系统默认。</summary>
    public string CurrentFontFamily { get; private set; }

    /// <summary>当前区域下已安装的字体族名，不含「默认字体」选项。</summary>
    public IReadOnlyList<string> FontNames { get; }

    /// <summary>保存下次启动使用的字体，不改当前窗口。</summary>
    public void Select(string fontFamily)
    {
        var normalized = new FontSettings { FontFamily = fontFamily }.Normalized().FontFamily;
        if (string.Equals(CurrentFontFamily, normalized, StringComparison.Ordinal)) return;

        CurrentFontFamily = normalized;
        OnPropertyChanged(nameof(CurrentFontFamily));
        _store.Save(new FontSettings { FontFamily = normalized });
    }
}
