using Bowen.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 把 <see cref="ReleaseNoteLine"/> 铺成可视元素。见 XAML 里那段「为什么不用 DataTemplate」。
/// </summary>
public sealed partial class ReleaseNotesView : UserControl
{
    /// <summary>项目符号与正文之间的间距。</summary>
    private const double MarkerSpacing = 8;

    public ReleaseNotesView() => InitializeComponent();

    /// <summary>
    /// 铺一遍内容。没有内容时整块收起来，不占一行空白。
    /// </summary>
    public void SetLines(IReadOnlyList<ReleaseNoteLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        Host.Children.Clear();

        // 控件自己的 Visibility 在 ScrollViewer 上，这里要拿到它。
        var scroller = (ScrollViewer)Content;
        scroller.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (lines.Count == 0)
        {
            return;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            Host.Children.Add(BuildLine(lines[index], index));
        }
    }

    private static FrameworkElement BuildLine(ReleaseNoteLine line, int index) => line.Kind switch
    {
        ReleaseNoteLineKind.Heading => BuildHeading(line),
        ReleaseNoteLineKind.Bullet => BuildBullet(line),
        _ => BuildParagraph(line),
    };

    private static TextBlock BuildHeading(ReleaseNoteLine line)
    {
        var block = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        AddSpans(block, line.Spans);
        return block;
    }

    private static TextBlock BuildParagraph(ReleaseNoteLine line)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AddSpans(block, line.Spans);
        return block;
    }

    /// <summary>
    /// 项目符号那一行。
    /// </summary>
    /// <remarks>
    /// <b>用两列 Grid 而不是给文本加左缩进</b>：折行时第二行要跟第一行的正文对齐，
    /// 而段落级的缩进做不到「只缩正文、不缩符号」，也不想为此去算一个像素级的悬挂缩进值。
    /// </remarks>
    private static FrameworkElement BuildBullet(ReleaseNoteLine line)
    {
        var row = new Grid { ColumnSpacing = MarkerSpacing };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new TextBlock
        {
            Text = "•",
            VerticalAlignment = VerticalAlignment.Top,

            // 稍微压暗一点，符号不该和正文抢注意力。用 Opacity 而不是指定画刷：
            // 画刷要从资源字典里取，而取出来的那个不跟着主题走。
            Opacity = 0.7,
        };

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AddSpans(text, line.Spans);

        Grid.SetColumn(marker, 0);
        Grid.SetColumn(text, 1);

        row.Children.Add(marker);
        row.Children.Add(text);
        return row;
    }

    private static void AddSpans(TextBlock block, IReadOnlyList<ReleaseNoteSpan> spans)
    {
        foreach (var span in spans)
        {
            block.Inlines.Add(new Run
            {
                Text = span.Text,
                FontWeight = span.IsBold ? FontWeights.SemiBold : FontWeights.Normal,
            });
        }
    }
}
