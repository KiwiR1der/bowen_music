namespace Bowen.Core.Models.Lyrics;

/// <summary>
/// 解析式弹簧：位置与速度都是时间的闭式函数，换目标时按当前速度接续。
/// </summary>
/// <remarks>
/// <para>
/// 参数与曲线取自 AMLL（<c>@applemusic-like-lyrics/core</c> 0.2.0 的纵向弹簧：
/// <c>mass 0.9 / damping 15 / stiffness 90</c>），求解器源自 pushkine 的 MIT 实现。
/// 阻尼比约 0.83，约 0.5 秒落位，带极小的过冲 —— 观感是软着陆，不是临界阻尼的硬停。
/// </para>
/// <para>
/// <b>解析解而不是逐帧积分</b>：位置只由换目标后经过的时间决定，帧率不影响轨迹；
/// 中途换目标从当前位置和速度重新起算，不会丢速度产生折角。
/// </para>
/// </remarks>
public sealed class LyricSpring
{
    private const double Mass = 0.9;
    private const double Damping = 15;
    private const double Stiffness = 90;
    private const double DampingFrequency = 4 * Mass * Stiffness - Damping * Damping;

    /// <summary>阻尼振荡角频率。</summary>
    private static readonly double Omega = Math.Sqrt(DampingFrequency) / (2 * Mass);

    /// <summary>指数衰减率。</summary>
    private static readonly double Alpha = -Damping / (2 * Mass);

    private double _position;
    private double _velocity;
    private double _from;
    private double _fromVelocity;
    private double _target;
    private double _elapsed;
    private bool _queued;
    private double _queuedTarget;
    private double _queuedDelay;

    public LyricSpring(double position = 0) => Reset(position);

    public double Position => _position;

    public double Velocity => _velocity;

    public double Target => _target;

    /// <summary>已经落位，且没有等待中的新目标。</summary>
    public bool IsSettled => !_queued
        && Math.Abs(_target - _position) < 0.01
        && Math.Abs(_velocity) < 0.01;

    /// <summary>立即落到指定位置，清空速度与排队目标。</summary>
    public void Reset(double position)
    {
        _position = _from = _target = position;
        _velocity = _fromVelocity = 0;
        _elapsed = 0;
        _queued = false;
    }

    /// <summary>
    /// 设置新目标。<paramref name="delay"/> 秒后才开始朝它运动，期间继续维持原目标。
    /// </summary>
    public void SetTarget(double target, double delay = 0)
    {
        if (delay > 0)
        {
            _queued = true;
            _queuedTarget = target;
            _queuedDelay = delay;
            return;
        }

        _queued = false;
        _target = target;
        _from = _position;
        _fromVelocity = _velocity;
        _elapsed = 0;
    }

    public void Update(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        _elapsed += seconds;
        (_position, _velocity) = Evaluate(_elapsed);

        if (_queued)
        {
            _queuedDelay -= seconds;

            if (_queuedDelay <= 0)
            {
                SetTarget(_queuedTarget);
            }
        }

        if (IsSettled)
        {
            _position = _target;
            _velocity = 0;
            _elapsed = 0;
            _from = _target;
            _fromVelocity = 0;
        }
    }

    private (double Position, double Velocity) Evaluate(double seconds)
    {
        var delta = _target - _from;
        var leftover = (Damping * delta - 2 * Mass * _fromVelocity) / Math.Sqrt(DampingFrequency);
        var decay = Math.Exp(Alpha * seconds);
        var cosine = Math.Cos(Omega * seconds);
        var sine = Math.Sin(Omega * seconds);

        // x(t) = target - (cos(dt)·Δ + sin(dt)·L)·e^(αt)
        var position = _target - (cosine * delta + sine * leftover) * decay;

        // 对时间求导，速度同样闭合，供下一次换目标接续。
        var velocity = -decay
            * (delta * ((Alpha * cosine) - (Omega * sine))
                + leftover * ((Omega * cosine) + (Alpha * sine)));
        return (position, velocity);
    }
}
