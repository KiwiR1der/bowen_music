namespace Bowen.Core.Models;

/// <summary>
/// 落盘的外观设置。是<b>持久化形状</b>，不是给界面直接用的模型。
/// </summary>
/// <remarks>
/// 单独包一层对象而不是把枚举名裸写进文件，是为了以后加字段时不用换格式 ——
/// 裸写一个字符串的话，加第二个设置项就是一次不兼容变更。
/// </remarks>
/// <param name="Theme">外观档位。</param>
/// <param name="AcrylicEffects">
/// 应用内的磨砂表面是否使用亚克力。<b>可为空是刻意的</b>：旧版设置文件没有这个字段，
/// 反序列化后是 <c>null</c>，读取方按「开启」处理；新版写入时总是显式给值。
/// </param>
/// <param name="StartupPage">
/// 启动后进入的侧栏根页。默认「我喜欢的」，与加入这个选项之前的行为一致；
/// 旧版设置文件缺字段时由构造参数默认值补上。
/// </param>
public sealed record ThemeSettings(
    AppTheme Theme,
    bool? AcrylicEffects = null,
    StartupSection StartupPage = StartupSection.Favorites)
{
    /// <summary>没有设置文件时用的默认值。亚克力效果默认开启，启动落点默认「我喜欢的」。</summary>
    public static ThemeSettings Default { get; } = new(AppTheme.System, AcrylicEffects: true);
}
