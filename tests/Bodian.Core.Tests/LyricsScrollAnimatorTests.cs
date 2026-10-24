using Bodian.WinUI.LyricRenderer;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 滚动动画：逐句弹簧、AMLL 的延迟曲线、单元内原文与译文同动。
/// </summary>
/// <remarks>
/// 曲线与参数对齐 LyciaMusic 使用的 AMLL（<c>@applemusic-like-lyrics/core</c> 0.2.0）。
/// 这里守的是「像 AMLL」而不是「像临界阻尼」：软落位带极小的过冲、换目标不丢速度、
/// 视口顶部先走、下方按 1.05 递减延迟。
/// </remarks>
public sealed class LyricsScrollAnimatorTests
{
    /// <summary>四句双行歌词：每句两行，句间 156 DIP，句高 130 DIP。</summary>
    private static readonly int[] PairMap = [0, 0, 1, 1, 2, 2, 3, 3];
    private static readonly float[] PairTops = [0, 156, 312, 468];
    private static readonly float[] PairHeights = [130, 130, 130, 130];

    private static LyricsScrollAnimator Create(double target = 0)
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(PairMap, PairTops, PairHeights, target);
        return animator;
    }

    private static void Advance(LyricsScrollAnimator animator, double seconds, int rate = 120)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds * rate));

        for (var frame = 0; frame < frames; frame++)
        {
            animator.Update(seconds / frames);
        }
    }

    [Fact]
    public void PairedLines_ShareOneOffsetDuringTheWholeScroll()
    {
        var animator = Create();
        animator.Retarget(156, 2);

        for (var frame = 0; frame < 240; frame++)
        {
            animator.Update(1.0 / 120);
            Assert.Equal(animator.OffsetAt(0), animator.OffsetAt(1));
            Assert.Equal(animator.OffsetAt(2), animator.OffsetAt(3));
            Assert.Equal(animator.OffsetAt(4), animator.OffsetAt(5));
        }
    }

    [Fact]
    public void EveryUnitSettlesOnTheSharedTarget()
    {
        var animator = Create();
        animator.Retarget(156, 2);

        Advance(animator, 3);

        for (var line = 0; line < PairMap.Length; line++)
        {
            Assert.Equal(156, animator.OffsetAt(line), precision: 2);
        }

        Assert.False(animator.IsAnimating);
    }

    /// <summary>视口顶部附近的句子先走，下方的句子按延迟晚起步。</summary>
    [Fact]
    public void LinesBelowTheViewportStartLaterThanTheOnesOnScreen()
    {
        var animator = Create();
        animator.Retarget(156, 0);

        // 第一帧：锚点所在的第 0 句已经开始，最下面的第 3 句还在延迟里。
        animator.Update(1.0 / 120);
        Assert.True(animator.OffsetAt(0) > 0);
        Assert.Equal(0, animator.OffsetAt(6));

        // 0.3 秒后所有可见句都已经起步。
        Advance(animator, 0.3);
        Assert.True(animator.OffsetAt(6) > 0);
    }

    /// <summary>浏览不带错峰：滚轮一动整块跟手。</summary>
    [Fact]
    public void BrowsingMovesEveryUnitImmediately()
    {
        var animator = Create();
        animator.Retarget(150, 0, stagger: false);
        animator.Update(1.0 / 120);

        for (var unit = 0; unit < PairTops.Length; unit++)
        {
            Assert.True(animator.OffsetAt(unit * 2) > 0);
        }
    }

    [Fact]
    public void ResetSnapsWithoutAnimation()
    {
        var animator = Create();
        animator.Retarget(400, 2);
        animator.Update(1.0 / 120);

        animator.Reset(PairMap, PairTops, PairHeights, 640);

        for (var line = 0; line < PairMap.Length; line++)
        {
            Assert.Equal(640, animator.OffsetAt(line));
        }

        Assert.Equal(640, animator.Target);
        Assert.False(animator.IsAnimating);
    }

    [Fact]
    public void SettledScrollStopsRequestingFrames()
    {
        var animator = Create();
        animator.Retarget(156, 0);
        Advance(animator, 3);

        Assert.False(animator.IsAnimating);
        animator.Update(1.0 / 120);
        Assert.False(animator.HasMoved);
    }

    [Fact]
    public void CountTracksTheDocumentLineCount()
    {
        Assert.Equal(PairMap.Length, Create().Count);

        var empty = new LyricsScrollAnimator();
        empty.Reset([], [], [], 0);
        Assert.Equal(0, empty.Count);
        Assert.False(empty.IsAnimating);
    }
}
