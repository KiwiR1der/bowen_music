namespace Bowen.WinUI.ViewModels;

/// <summary>字体下拉的分组。按字形覆盖判定，不按名字语言。</summary>
public enum FontGroup
{
    /// <summary>「默认字体」，不指向具体家族。</summary>
    Default = 0,

    /// <summary>覆盖中日韩字形。</summary>
    Chinese = 1,

    /// <summary>只覆盖拉丁字形。</summary>
    Western = 2,

    /// <summary>符号字体，或既不覆盖中文也不覆盖拉丁。</summary>
    Symbol = 3,

    /// <summary>已保存但本机找不到的字体。</summary>
    Missing = 4,
}

/// <summary>
/// 字体下拉项。
/// </summary>
/// <remarks>
/// <b>分组看字形覆盖，不看名字语言</b>：<c>Microsoft YaHei UI</c> 名字是英文，
/// 但完全支持中文；反过来也有中文字体名只覆盖西文字形的情况。
/// 多语言字体只出现一次，归到它覆盖的最高优先级分组。
/// </remarks>
public sealed record FontOption(
    string FamilyName,
    string CanonicalName,
    string DisplayName,
    FontGroup Group)
{
    /// <summary>「默认字体」项。</summary>
    public static FontOption Default { get; } =
        new(string.Empty, string.Empty, "默认字体", FontGroup.Default);

    /// <summary>下拉显示文本：分组前缀 + 本地化家族名。</summary>
    public string Label => Group == FontGroup.Default
        ? DisplayName
        : $"{GroupLabel} · {DisplayName}";

    /// <summary>分组标题，给分组列表的表头用。</summary>
    public string GroupLabel => Group switch
    {
        FontGroup.Default => "默认",
        FontGroup.Chinese => "中文",
        FontGroup.Western => "西文",
        FontGroup.Symbol => "符号",
        FontGroup.Missing => "未安装",
        _ => string.Empty,
    };
}
