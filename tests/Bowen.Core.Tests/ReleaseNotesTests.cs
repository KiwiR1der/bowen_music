using Bowen.Core.Models;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 发布正文的切分。**纯函数，喂字符串就行** —— 不必联网，也不必真的发一个版本。
/// </summary>
public sealed class ReleaseNotesTests
{
    /// <summary>
    /// 正文形状照抄真实的 Release（2026-10-14 取自已发布的 v0.1.0）：
    /// 行尾是 <c>\r\n</c> 与 <c>\n</c> 混着的 —— 标题与空行是 CRLF，表格行是 LF。
    /// </summary>
    private const string Body =
        "## 0.1.0 — 2026-10-08\r\n\r\n首个公开版本。\r\n\r\n### 新增\r\n\r\n" +
        "- **系统媒体控制（SMTC）** —— 播放状态交给系统面板。\r\n" +
        "- 普通的一条，没有粗体。\r\n\r\n### 已修复\r\n\r\n- 某处闪一下。\r\n\r\n" +
        "### 校验\r\n\r\n| 文件 | SHA-256 |\n| --- | --- |\n" +
        "| `BowenMusic_0.1.0_x64_setup.exe` | `2586c1c8128ca869253747546fd6fd2dd606f1e3db9e18e537f27a52ef94ec67` |\r\n";

    [Fact]
    public void Parse_DropsTheVersionHeading()
    {
        var lines = ReleaseNotes.Parse(Body);

        // 版本号对话框自己有一处，正文里再显示一遍是重复。
        Assert.DoesNotContain(lines, line => line.Text.Contains("2026-10-08", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_DropsTheChecksumSectionEntirely()
    {
        var lines = ReleaseNotes.Parse(Body);

        Assert.DoesNotContain(lines, line => line.Text.Contains("SHA-256", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Text.Contains("BowenMusic", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_KeepsHeadingsBulletsAndParagraphs()
    {
        var lines = ReleaseNotes.Parse(Body);

        ReleaseNoteLineKind[] expected =
        [
            ReleaseNoteLineKind.Paragraph,  // 首个公开版本。
            ReleaseNoteLineKind.Heading,    // 新增
            ReleaseNoteLineKind.Bullet,
            ReleaseNoteLineKind.Bullet,
            ReleaseNoteLineKind.Heading,    // 已修复
            ReleaseNoteLineKind.Bullet,
        ];

        Assert.Equal(expected, lines.Select(line => line.Kind));
    }

    [Fact]
    public void Parse_SplitsBoldSpans()
    {
        var bullet = ReleaseNotes.Parse(Body)
            .First(line => line.Kind == ReleaseNoteLineKind.Bullet);

        Assert.Equal(2, bullet.Spans.Count);
        Assert.True(bullet.Spans[0].IsBold);
        Assert.Equal("系统媒体控制（SMTC）", bullet.Spans[0].Text);

        // 粗体之后那一段连同前导空格与「——」一起是普通文本（行内空格原样保留）。
        Assert.False(bullet.Spans[1].IsBold);
        Assert.Equal(" —— 播放状态交给系统面板。", bullet.Spans[1].Text);
    }

    [Fact]
    public void Parse_LineWithoutBoldIsASingleSpan()
    {
        var lines = ReleaseNotes.Parse(Body);
        var plain = lines.First(line => line.Text.StartsWith("普通的一条", StringComparison.Ordinal));

        var span = Assert.Single(plain.Spans);
        Assert.False(span.IsBold);
    }

    [Fact]
    public void Parse_UnpairedBoldMarkerDoesNotSwallowTheRest()
    {
        var lines = ReleaseNotes.Parse("## 1.0.0\n\n- **少了半个星号\n");

        // 星号没成对时按普通字符处理，半行字不能凭空消失。
        Assert.Contains("少了半个星号", lines[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_EmptyBodyGivesNothing(string? body)
    {
        Assert.Empty(ReleaseNotes.Parse(body));
    }

    [Fact]
    public void Parse_BodyWithoutAVersionHeadingStillRenders()
    {
        // 格式变了、或正文是手写的时候，从第一行开始显示，而不是给一个空白面板。
        var lines = ReleaseNotes.Parse("### 新增\n\n- 一条。\n");

        Assert.Equal(2, lines.Count);
        Assert.Equal(ReleaseNoteLineKind.Heading, lines[0].Kind);
    }

    [Fact]
    public void Parse_SkipsHorizontalRulesAndTables()
    {
        var lines = ReleaseNotes.Parse("## 1.0.0\n\n---\n\n| a | b |\n\n正文。\n");

        var line = Assert.Single(lines);
        Assert.Equal("正文。", line.Text);
    }
}
