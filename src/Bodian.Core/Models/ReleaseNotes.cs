namespace Bodian.Core.Models;

/// <summary>发布说明里一行的形态。</summary>
public enum ReleaseNoteLineKind
{
    /// <summary><c>### 新增</c> 这类小标题。</summary>
    Heading,

    /// <summary><c>- …</c> 这类项目符号。</summary>
    Bullet,

    /// <summary>普通段落。</summary>
    Paragraph,
}

/// <summary>行内的一段。只有「是不是粗体」一种属性 —— 正文里只用到了这一种行内标记。</summary>
public sealed record ReleaseNoteSpan(string Text, bool IsBold);

/// <summary>发布说明里的一行，已切成可直接渲染的片段。</summary>
public sealed record ReleaseNoteLine(ReleaseNoteLineKind Kind, IReadOnlyList<ReleaseNoteSpan> Spans)
{
    /// <summary>整行的纯文本。给无障碍名称与搜索用。</summary>
    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// 把 Release 正文切成可渲染的行。
/// </summary>
/// <remarks>
/// <para>
/// <b>只支持三种语法</b>（<c>###</c> 小标题、<c>-</c> 项目符号、<c>**粗体**</c>），
/// 因为 <c>changes.md</c> 的正文就只用这三种。为此引入一整套 markdown 解析与渲染不划算 ——
/// 本项目对体积是计较的（见 <c>docs/size-optimization.md</c>）。
/// </para>
/// <para>
/// <b>两处刻意的截断：</b>
/// <list type="bullet">
/// <item>开头的 <c>## &lt;版本号&gt; — &lt;日期&gt;</c> 不显示 —— 版本号对话框自己有一处，重复一遍是噪音；</item>
/// <item><c>### 校验</c> 及其后的 SHA-256 表格整段丢掉 —— 校验和是应用自己用的，不是给用户读的。</item>
/// </list>
/// </para>
/// <para>
/// <b>纯函数</b>：喂一段真实正文就能离线单测，不必联网、也不必真的发一个版本。
/// </para>
/// </remarks>
public static class ReleaseNotes
{
    /// <summary>正文里「校验」那一节的标题。它之后的内容一律不显示。</summary>
    private const string ChecksumSectionHeading = "### 校验";

    public static IReadOnlyList<ReleaseNoteLine> Parse(string? body)
    {
        var result = new List<ReleaseNoteLine>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return result;
        }

        var lines = body.Replace("\r\n", "\n").Split('\n');

        // 从版本标题之后开始。找不到标题（正文是手写的、或格式变了）就从头开始，
        // 宁可多显示一行，也不要给用户一个空白面板。
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith('|') || IsHorizontalRule(line))
            {
                continue;
            }

            if (line.StartsWith(ChecksumSectionHeading, StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Heading, ParseSpans(line[4..].Trim())));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                result.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Bullet, ParseSpans(line[2..].Trim())));
                continue;
            }

            result.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Paragraph, ParseSpans(line)));
        }

        return result;
    }

    /// <summary>把 <c>**x**</c> 切成粗体片段与普通片段。</summary>
    /// <remarks>
    /// <b>不成对的 <c>**</c> 按普通字符处理</b>（后面的 <c>bold</c> 翻不回来就停在那里），
    /// 不会因为正文里少写一个星号就丢掉半行字。
    /// </remarks>
    private static List<ReleaseNoteSpan> ParseSpans(string text)
    {
        var spans = new List<ReleaseNoteSpan>();
        var bold = false;
        var start = 0;

        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] != '*' || text[i + 1] != '*')
            {
                continue;
            }

            if (i > start)
            {
                spans.Add(new ReleaseNoteSpan(text[start..i], bold));
            }

            bold = !bold;
            i++;                // 吃掉第二个星号，循环那次自增再跨过它
            start = i + 1;
        }

        if (start < text.Length)
        {
            spans.Add(new ReleaseNoteSpan(text[start..], bold));
        }

        return spans;
    }

    private static bool IsHorizontalRule(string line) =>
        line is "---" or "***" or "___";
}
