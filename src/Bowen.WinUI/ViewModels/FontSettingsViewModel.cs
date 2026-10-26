using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization;

namespace Bowen.WinUI.ViewModels;

/// <summary>界面字体偏好。设置只落盘，应用重启后统一应用。</summary>
public sealed partial class FontSettingsViewModel : ObservableObject
{
    private readonly IFontSettingsStore _store;
    private readonly ILogger<FontSettingsViewModel> _logger;

    public FontSettingsViewModel(IFontSettingsStore store, ILogger<FontSettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        CurrentFontFamily = store.Load().Normalized().FontFamily;

        // ApplicationLanguages 返回的是 zh-Hans-CN。部分 Win2D/DirectWrite 版本按字面匹配，
        // 认不出带 script 的标签，会退回 en-US；先显式放 zh-CN。
        var locales = new List<string> { "zh-CN", "zh-Hans-CN" };
        foreach (var locale in ApplicationLanguages.Languages)
        {
            if (!locales.Any(item => string.Equals(item, locale, StringComparison.OrdinalIgnoreCase)))
                locales.Add(locale);
        }
        if (!locales.Any(item => string.Equals(item, "en-US", StringComparison.OrdinalIgnoreCase)))
            locales.Add("en-US");

        var localized = CanvasTextFormat.GetSystemFontFamilies(locales)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        // 旧版本可能存的是 en-US 名字。两份列表同源同序，按下标换成中文名，
        // 避免下拉里同时出现「Alibaba PuHuiTi 3.0」和「阿里巴巴普惠体 3.0」。
        if (localized.Length > 0 && !string.IsNullOrEmpty(CurrentFontFamily))
        {
            var canonical = CanvasTextFormat.GetSystemFontFamilies();
            if (canonical.Length == localized.Length)
            {
                var index = Array.FindIndex(canonical,
                    name => string.Equals(name, CurrentFontFamily, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) CurrentFontFamily = localized[index];
            }
        }

        // 在 DI 首次解析本服务时执行；组合根保证它发生在主窗口创建之前。
        if (!string.IsNullOrEmpty(CurrentFontFamily))
            Application.Current.Resources["ContentControlThemeFontFamily"] = new FontFamily(CurrentFontFamily);

        IEnumerable<string> installed = localized;
        if (!string.IsNullOrEmpty(CurrentFontFamily)) installed = installed.Append(CurrentFontFamily);
        FontNames = [.. installed
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

        _logger.LogInformation(
            "字体列表：{Count} 个，语言 {Locales}，中文名 {Cjk} 个，当前 {Current}",
            FontNames.Count, string.Join(",", locales),
            FontNames.Count(name => name.Any(IsCjk)), CurrentFontFamily);
    }

    private static bool IsCjk(char c) => c >= 0x4E00 && c <= 0x9FFF;

    /// <summary>已保存的字体族名；空串 = 跟随系统默认。</summary>
    public string CurrentFontFamily { get; private set; }

    /// <summary>当前区域下已安装的字体族名，不含「默认字体」选项。</summary>
    public IReadOnlyList<string> FontNames { get; }

    /// <summary>
    /// 保存下次启动使用的字体，不改当前窗口。返回是否写入成功。
    /// </summary>
    /// <remarks>
    /// <b>保存后发一条重启提示</b>：改动对当前窗口完全不可见，用户会以为没保存上。
    /// 通知走 <see cref="INoticeSink"/>，注入方式与其它动作入口一致（见 App 组合根）。
    /// 写入失败时不更新已保存的选择，也不报成功。
    /// </remarks>
    public bool Select(string fontFamily)
    {
        var normalized = new FontSettings { FontFamily = fontFamily }.Normalized().FontFamily;
        if (string.Equals(CurrentFontFamily, normalized, StringComparison.Ordinal)) return true;

        if (!_store.Save(new FontSettings { FontFamily = normalized }))
        {
            _notice?.Show("界面字体未能保存，重启后不会生效", NoticeSeverity.Error);
            return false;
        }

        var isDefault = string.IsNullOrEmpty(normalized);
        CurrentFontFamily = normalized;
        OnPropertyChanged(nameof(CurrentFontFamily));

        _notice?.Show(isDefault
            ? "界面字体已恢复默认，重启应用后生效"
            : $"界面字体已保存为「{normalized}」，重启应用后生效", NoticeSeverity.Success);
        return true;
    }

    /// <summary>
    /// 通知出口。构造函数注入会形成循环（通知条依赖主窗口、主窗口构造依赖字体已应用），
    /// 所以由组合根在主窗口创建后回填 —— 与 <see cref="ThemeViewModel.Transition"/> 同一条接线思路。
    /// </summary>
    internal INoticeSink? Notice { get => _notice; set => _notice = value; }
    private INoticeSink? _notice;
}
