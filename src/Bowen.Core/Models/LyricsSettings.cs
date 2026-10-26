namespace Bowen.Core.Models;

/// <summary>
/// 歌词页的偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="DesktopLyricsSettings"/> 分开一份文件</b>：那份管桌面歌词条的外观，
/// 这份管全屏歌词页显示什么 —— 不同界面的偏好挤进一个文件，只会让读写互相牵制。
/// </para>
/// <para>
/// <b>译文不是单独一条轨。</b> 外文歌那份歌词内容里原文与译文成对出现，解析时就分好了
/// （<see cref="Lyrics.LyricLine.IsTranslation"/>）。这里的开关只决定要不要把它们
/// 从文档里剔掉，<b>不产生任何网络请求</b>。
/// </para>
/// </remarks>
public sealed record LyricsSettings
{
    /// <summary>
    /// 歌词页是否显示译文。
    /// </summary>
    /// <remarks>
    /// <b>默认开。</b> 译文本来就在歌词内容里，客户端此前一直显示着；
    /// 这个开关的意义是给用户一个关掉它的办法，不是新增一种显示。
    /// </remarks>
    public bool ShowTranslation { get; init; } = true;

    /// <summary>
    /// 全屏歌词的字体族名；空串表示跟随系统默认。
    /// </summary>
    /// <remarks>
    /// <b>只存名字，不校验是否存在。</b> 缺字体时渲染层回退系统默认，与界面字体同一条规矩。
    /// 改它即时生效（重排当前文档），不需要重启。
    /// </remarks>
    public string FontFamily { get; init; } = string.Empty;

    /// <summary>默认设置：显示译文、跟随系统字体。</summary>
    public static LyricsSettings Default { get; } = new();

    /// <summary>规范化字体名，保持空字符串 = 渲染层默认的约定。</summary>
    /// <remarks>
    /// 保留这个入口与其他设置同一个形状：新增需要夹取范围的偏好时不必改 store 的调用点。
    /// </remarks>
    public LyricsSettings Normalized() => this with { FontFamily = FontFamily?.Trim() ?? string.Empty };
}
