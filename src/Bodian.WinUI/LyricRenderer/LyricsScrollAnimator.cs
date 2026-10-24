using Bodian.Core.Models.Lyrics;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>每个滚动单元独立弹簧落位，按 AMLL 的延迟曲线错峰起步。</summary>
/// <remarks>
/// <para>
/// 曲线对齐 LyciaMusic 使用的 AMLL：基础延迟 50 ms 从视口顶部向下逐句累加，
/// 当前句及之后的每一句把基础延迟除以 1.05，于是上方先走、下方后走，形成波浪。
/// 每个单元是一个 <see cref="LyricSpring"/>，换目标时按当前速度接续，不会顿一下再走。
/// </para>
/// <para>
/// 同一句的原文与译文共用一个单元，两行共用同一个弹簧位置，滚动中不会拉开。
/// </para>
/// </remarks>
internal sealed class LyricsScrollAnimator
{
    /// <summary>视口顶部第一句的起步延迟。</summary>
    private const double BaseDelay = 0.05;

    /// <summary>当前句以下每句把基础延迟缩小的比例。</summary>
    private const double DelayDecay = 1.05;

    private LyricSpring[] _springs = [];
    private int[] _unitOfLine = [];
    private float[] _unitTops = [];
    private float[] _unitHeights = [];

    /// <summary>文档里的行数。渲染器用它判断排版是否换了一份。</summary>
    public int Count => _unitOfLine.Length;

    public bool IsAnimating { get; private set; }

    public bool HasMoved { get; private set; }

    public double Target { get; private set; }

    /// <summary>整份排版重建时立即落位，不做动画。</summary>
    public void Reset(int[] unitOfLine, float[] unitTops, float[] unitHeights, double target)
    {
        _unitOfLine = unitOfLine;
        _unitTops = unitTops;
        _unitHeights = unitHeights;

        var units = unitOfLine.Length == 0 ? 0 : unitOfLine[^1] + 1;
        _springs = new LyricSpring[units];

        for (var unit = 0; unit < units; unit++)
        {
            _springs[unit] = new LyricSpring(target);
        }

        Target = target;
        IsAnimating = HasMoved = false;
    }

    /// <param name="target">新的滚动偏移，所有单元共用。</param>
    /// <param name="anchorLine">播放行下标，内部换算成它所属的单元。</param>
    /// <param name="stagger">是否按句错峰。浏览时不启用，整块一起跟手。</param>
    public void Retarget(double target, int anchorLine, bool stagger = true)
    {
        if (target == Target)
        {
            return;
        }

        Target = target;
        var anchorUnit = anchorLine >= 0 && anchorLine < _unitOfLine.Length
            ? _unitOfLine[anchorLine]
            : 0;
        var delay = 0.0;
        var baseDelay = stagger ? BaseDelay : 0.0;

        for (var unit = 0; unit < _springs.Length; unit++)
        {
            _springs[unit].SetTarget(target, delay);

            // 下一个单元的顶边进入视口后，延迟才开始累加。
            var nextTop = unit + 1 < _springs.Length
                ? _unitTops[unit + 1]
                : _unitTops[unit] + _unitHeights[unit];

            if (stagger && nextTop - target >= 0)
            {
                delay += baseDelay;

                if (unit >= anchorUnit)
                {
                    baseDelay /= DelayDecay;
                }
            }
        }

        IsAnimating = true;
    }

    public void Update(double seconds)
    {
        HasMoved = false;
        var animating = false;

        for (var unit = 0; unit < _springs.Length; unit++)
        {
            var previous = _springs[unit].Position;
            _springs[unit].Update(seconds);
            HasMoved |= previous != _springs[unit].Position;
            animating |= !_springs[unit].IsSettled;
        }

        IsAnimating = animating;
    }

    /// <summary>某一行所属单元的当前滚动偏移；同一单元内的行返回同一个值。</summary>
    public double OffsetAt(int lineIndex) => _springs[_unitOfLine[lineIndex]].Position;
}
