using Bodian.Core.Models.Lyrics;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 解析式弹簧：落位、速度接续、帧率一致、延迟目标与过冲上限。
/// </summary>
/// <remarks>
/// 参数对齐 AMLL 的纵向弹簧（mass 0.9 / damping 15 / stiffness 90），
/// 阻尼比约 0.83，过冲不到 1%。改参数会改变观感，所以这里把特征钉住。
/// </remarks>
public sealed class LyricSpringTests
{
    private const double Rate = 120;

    private static void Advance(LyricSpring spring, double seconds, double rate = Rate)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds * rate));

        for (var frame = 0; frame < frames; frame++)
        {
            spring.Update(seconds / frames);
        }
    }

    [Fact]
    public void ReachesTargetAndSettles()
    {
        var spring = new LyricSpring(0);
        spring.SetTarget(100);

        Advance(spring, 3);

        Assert.Equal(100, spring.Position);
        Assert.True(spring.IsSettled);
    }

    /// <summary>中途换目标时位置和速度都连续，不会在换向瞬间折角。</summary>
    [Fact]
    public void KeepsVelocityWhenTheTargetChanges()
    {
        var spring = new LyricSpring(0);
        spring.SetTarget(100);
        Advance(spring, 0.15);

        var position = spring.Position;
        var velocity = spring.Velocity;
        spring.SetTarget(300);
        spring.Update(0.0001);

        Assert.Equal(position + velocity * 0.0001, spring.Position, precision: 3);
    }

    [Fact]
    public void SameTimeProducesTheSamePositionAtAnyFrameRate()
    {
        var fast = new LyricSpring(0);
        fast.SetTarget(200);
        Advance(fast, 0.2, rate: 240);

        var slow = new LyricSpring(0);
        slow.SetTarget(200);
        Advance(slow, 0.2, rate: 60);

        Assert.Equal(fast.Position, slow.Position, precision: 6);
        Assert.Equal(fast.Velocity, slow.Velocity, precision: 4);
    }

    [Fact]
    public void DelayedTargetWaitsForItsDelay()
    {
        var spring = new LyricSpring(0);
        spring.SetTarget(100, delay: 0.2);

        Advance(spring, 0.15);
        Assert.Equal(0, spring.Position);
        Assert.False(spring.IsSettled);

        Advance(spring, 0.15);
        Assert.True(spring.Position > 0);

        Advance(spring, 3);
        Assert.Equal(100, spring.Position);
    }

    /// <summary>AMLL 的弹簧过冲极小：100 DIP 的位移不应越过 2 DIP。</summary>
    [Fact]
    public void OvershootStaysBelowTwoPercent()
    {
        var spring = new LyricSpring(0);
        spring.SetTarget(100);
        var maximum = 0.0;

        for (var frame = 0; frame < 240; frame++)
        {
            spring.Update(1.0 / Rate);
            maximum = Math.Max(maximum, spring.Position);
        }

        Assert.InRange(maximum, 100, 102);
    }

    [Fact]
    public void ResetClearsVelocityAndQueuedTarget()
    {
        var spring = new LyricSpring(0);
        spring.SetTarget(300);
        Advance(spring, 0.1);
        spring.SetTarget(500, delay: 0.5);

        spring.Reset(40);

        Assert.Equal(40, spring.Position);
        Assert.Equal(0, spring.Velocity);
        Assert.True(spring.IsSettled);
    }
}
