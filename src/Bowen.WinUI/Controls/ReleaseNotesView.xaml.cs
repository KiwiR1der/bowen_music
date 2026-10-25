using Bowen.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 把 <see cref="ReleaseNoteLine"/> 铺成可视元素。见 XAML 里那段「为什么不用 DataTemplate」。
/// </summary>
public sealed partial class ReleaseNotesView : UserControl
{
    /// <summary>项目符号与正文之间的间距。</summary>
    private const double MarkerSpacing = 8;

    /// <summary>
    /// 小节标题字号。介于 <c>FontSizeBodyLarge</c>（14）与 <c>FontSizeTitle</c>（18）之间：
    /// 14 与正文 13 只差一号、看不出来，18 又逼近弹窗标题。
    /// </summary>
    private const double HeadingFontSize = 16;

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

    private FrameworkElement BuildLine(ReleaseNoteLine line, int index) => line.Kind switch
    {
        ReleaseNoteLineKind.Heading => BuildHeading(line),
        ReleaseNoteLineKind.Bullet => BuildBullet(line),
        ReleaseNoteLineKind.Quote => BuildQuote(line),
        _ => BuildParagraph(line),
    };

    private static TextBlock BuildHeading(ReleaseNoteLine line)
    {
        var block = new TextBlock
        {
            // 正文 13，标题 16 粗体。半粗在中文上会落到常规字重，看起来像没加粗。
            FontSize = HeadingFontSize,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 6, 0, 0),
        };

        AddSpans(block, line.Spans);
        return block;
    }

    private static TextBlock BuildParagraph(ReleaseNoteLine line)
    {
        var block = new TextBlock
        {
            FontSize = Token("FontSizeBody"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };

        AddSpans(block, line.Spans);
        return block;
    }

    /// <summary>从 <c>Tokens.xaml</c> 取字号标量。</summary>
    private static double Token(string key) => (double)Application.Current.Resources[key];

    /// <summary>
    /// 引用块：左侧一条强调色竖条，正文用次级色，与正常段落拉开层次。
    /// </summary>
    private FrameworkElement BuildQuote(ReleaseNoteLine line)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border { Style = (Style)Resources["QuoteBarStyle"], Margin = new Thickness(0, 1, 0, 1) };
        var text = new TextBlock { Style = (Style)Resources["QuoteTextStyle"] };
        AddSpans(text, line.Spans);

        Grid.SetColumn(bar, 0);
        Grid.SetColumn(text, 1);

        row.Children.Add(bar);
        row.Children.Add(text);
        return row;
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
            FontSize = Token("FontSizeBody"),
            VerticalAlignment = VerticalAlignment.Top,
            LineHeight = 20,

            // 稍微压暗一点，符号不该和正文抢注意力。用 Opacity 而不是指定画刷：
            // 画刷要从资源字典里取，而取出来的那个不跟着主题走。
            Opacity = 0.7,
        };

        var text = new TextBlock
        {
            FontSize = Token("FontSizeBody"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };
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
            var run = new Run
            {
                Text = span.Text,

                // 中文内容里 SemiBold 会落到常规字重，粗体标记必须用 Bold 才看得出来。
                FontWeight = span.IsBold ? FontWeights.Bold : FontWeights.Normal,
            };

            if (span.IsCode)
            {
                // Run 没有 Background，代码靠等宽字体与略小的字号区分。
                // Cascadia Mono 不在 Win10 上，回退到 Consolas。
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                run.FontSize = 13;
            }

            block.Inlines.Add(run);
        }
    }
}
