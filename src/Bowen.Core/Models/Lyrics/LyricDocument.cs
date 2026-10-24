namespace Bowen.Core.Models.Lyrics;

/// <summary>歌词版式。</summary>
public enum LyricKind
{
    /// <summary>没有歌词。请求成功但服务端给的是空串时就是这个。</summary>
    None = 0,

    /// <summary>逐行。只有行时间轴，行内无逐字。</summary>
    LineByLine = 1,

    /// <summary>逐字。行内有音节级时间轴。</summary>
    WordByWord = 2,
}

/// <summary>
/// 一首歌的歌词。渲染层唯一需要认识的类型。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是本项目自建的统一模型</b>，不依赖任何第三方歌词库：源生成的那几个库都不支持
/// AWLRC（行内 <c>&lt;a,b&gt;</c>），解析本来就得自己写，只借它们的模型容器得不偿失
/// （见 <c>docs/lyrics-ui.md</c> 第 1 节与计划里的决策①）。
/// </para>
/// <para>
/// <b>空文档不是异常</b>：服务端在「这首歌没有逐字轨」时返回空串，业务码依然是 200。
/// 用 <see cref="Empty"/> 表达，调用方不必区分「没歌词」和「取词失败」——
/// 对界面来说两者都是「这首歌没词」。
/// </para>
/// </remarks>
/// <param name="Lines">按时间升序的行。空文档时是空数组。</param>
/// <param name="Kind">版式。</param>
public sealed record LyricDocument(IReadOnlyList<LyricLine> Lines, LyricKind Kind)
{
    /// <summary>没有歌词。</summary>
    public static LyricDocument Empty { get; } = new([], LyricKind.None);

    /// <summary>没有歌词。</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// 文档里有没有译文行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 界面拿它决定「显示译文」那颗开关能不能点。中文歌、以及逐行版认不出译文的歌
    /// （见 <c>BodianLyricParser.MarkTranslations</c>）都没有，点了不会有任何变化。
    /// </para>
    /// <para>
    /// <b>要在完整文档上问这个问题</b>：译文行在 <see cref="WithoutTranslations"/> 之后就没了，
    /// 拿剔除过的文档来问永远是 <c>false</c>。
    /// </para>
    /// </remarks>
    public bool HasTranslation
    {
        get
        {
            foreach (var line in Lines)
            {
                if (line.IsTranslation)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 剔除没有文字的行。本来就没有空行时<b>返回自身</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 歌词内容里常有空白行：外文歌的译文占位、段落分隔、结尾空行。它们没有可显示的内容，
    /// 但参与排版就会占掉一整行 —— 界面上表现为凭空空一行。
    /// </para>
    /// <para>
    /// <b>不重算 <see cref="LyricLine.Start"/> 与 <see cref="LyricLine.Duration"/>。</b>
    /// 剔除空行不改变任何非空行自己的时间窗；<see cref="IndexOfLineAt"/> 在原来的空行时段里
    /// 会继续返回上一句，比高亮消失更自然。
    /// </para>
    /// </remarks>
    public LyricDocument WithoutBlankLines()
    {
        var kept = new List<LyricLine>(Lines.Count);
        var dropped = false;

        foreach (var line in Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                dropped = true;
                continue;
            }

            kept.Add(line);
        }

        return dropped ? new LyricDocument(kept, Kind) : this;
    }

    /// <summary>
    /// 剔除译文行后的文档。本来就没有译文行时<b>返回自身</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>不重算 <see cref="LyricLine.Duration"/>。</b> 成对的两行行首相同，剔掉其中一条
    /// 既不改变另一条的 <see cref="LyricLine.Start"/>，也不改变「下一行行首 − 本行行首」，
    /// 所以逐字轴、滚动与 <see cref="IndexOfLineAt"/> 的语义原样保留。
    /// </para>
    /// <para>
    /// <b>反过来把译文留在文档里、让渲染层逐行跳过是错的</b> —— 行高与滚动位置会跟着错。
    /// 「显不显示译文」必须在文档这一层决定。
    /// </para>
    /// </remarks>
    public LyricDocument WithoutTranslations()
    {
        var kept = new List<LyricLine>(Lines.Count);
        var dropped = false;

        foreach (var line in Lines)
        {
            if (line.IsTranslation)
            {
                dropped = true;
                continue;
            }

            kept.Add(line);
        }

        return dropped ? new LyricDocument(kept, Kind) : this;
    }

    /// <summary>
    /// 二分查找 <paramref name="position"/> 落在哪一行。
    /// </summary>
    /// <returns>行下标；位置在第一行之前时返回 <c>-1</c>。</returns>
    /// <remarks>
    /// 唱片头空白段时返回 <c>-1</c>，界面据此不高亮任何一行。
    /// 曲末之后仍返回最后一行——那时保持末行高亮比清空更自然。
    /// </remarks>
    public int IndexOfLineAt(TimeSpan position)
    {
        var low = 0;
        var high = Lines.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (Lines[mid].Start <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// 每一行属于哪个滚动单元。同一句原文与紧邻的译文共用一个单元。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 滚动动画按<b>单元</b>而不是按<b>行</b>推进：一句原文和它的译文必须刚性同动。
    /// 逐行错峰会让译文比原文晚一步起步，滚动词条的双行在运动中散开。
    /// </para>
    /// <para>
    /// 译文跟在原文后面时并入上一句；文件开头的译文前面没有原文，并入它后面的第一句。
    /// 返回的下标单调不减，单元总数等于末项加一。
    /// </para>
    /// </remarks>
    public int[] BuildScrollUnitMap()
    {
        var map = new int[Lines.Count];
        var leading = new List<int>();
        var unit = -1;

        for (var i = 0; i < map.Length; i++)
        {
            if (!Lines[i].IsTranslation)
            {
                unit++;
                map[i] = unit;

                foreach (var pending in leading)
                {
                    map[pending] = unit;
                }

                leading.Clear();
                continue;
            }

            if (unit < 0)
            {
                leading.Add(i);
            }
            else
            {
                map[i] = unit;
            }
        }

        // 整份文档都是译文时各自成单元，保证映射里没有空位。
        foreach (var pending in leading)
        {
            map[pending] = ++unit;
        }

        return map;
    }

    /// <summary>
    /// 某一行对应的原文行下标；它本身就是原文时返回自身。
    /// </summary>
    /// <returns>行下标；越界时返回 <c>-1</c>。</returns>
    /// <remarks>
    /// <b>译文行的时间戳不属于它自己。</b> 上游把译文排在原文的下一行，时间戳却取再下一句的
    /// 行首（见 <c>LyricTranslationTests.PairedSample</c>：1.547 的「夢ならば」下面紧跟
    /// 2.880 的译文「如果只是一场梦」，而 2.880 也是「どれほど」的行首）。
    /// 所以点译文行不能拿它自己的 <see cref="LyricLine.Start"/> 跳转，要回到上方最近的原文行，
    /// 否则会跳到下一句。
    /// </remarks>
    public int SourceLineIndex(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= Lines.Count)
        {
            return -1;
        }

        for (var index = lineIndex; index >= 0; index--)
        {
            if (!Lines[index].IsTranslation)
            {
                return index;
            }
        }

        return lineIndex;
    }
}
