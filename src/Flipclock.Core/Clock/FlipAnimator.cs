namespace Flipclock.Core.Clock;

public readonly record struct FlipTiming(double DurationMs, double CascadeMs, double Bounce);

/// <summary>Flap rotation over a flip, in degrees: 0 = upright, 180 = resting on the bottom half.</summary>
public static class FlapMotion
{
    const double MaxBounceDegrees = 22;

    /// <summary>Fraction of the flip duration at which the flap first lands.</summary>
    public static double LandFraction(double bounce) => 1 - 0.32 * Math.Clamp(bounce, 0, 1);

    public static double Angle(double progress, double bounce)
    {
        progress = Math.Clamp(progress, 0, 1);
        double land = LandFraction(bounce);
        if (progress < land)
        {
            // Gravity-like fall: slow release from the top, accelerating into the landing.
            double p = progress / land;
            return 180 * p * p;
        }
        if (land >= 1) return 180;
        // Two decaying rebounds off the bottom half.
        double q = (progress - land) / (1 - land);
        double amp = MaxBounceDegrees * Math.Clamp(bounce, 0, 1);
        return 180 - amp * Math.Abs(Math.Sin(2 * Math.PI * q)) * Math.Pow(1 - q, 1.6) * (q < 0.5 ? 1 : 0.45);
    }
}

/// <summary>What a single tile should show at a given instant.</summary>
public readonly record struct TileVisual(
    int StaticTopDigit,
    int StaticBottomDigit,
    bool FlapVisible,
    double FlapAngle,
    int FlapFrontDigit,
    int FlapBackDigit);

/// <summary>Tracks the per-tile split-flap animation state. Times are in milliseconds on any monotonic clock.</summary>
public sealed class FlipAnimator
{
    readonly int[] _from = new int[ClockDigits.Count];
    readonly int[] _to = new int[ClockDigits.Count];
    readonly double[] _start = new double[ClockDigits.Count];
    readonly double[] _duration = new double[ClockDigits.Count];
    readonly double[] _bounce = new double[ClockDigits.Count];
    readonly bool[] _active = new bool[ClockDigits.Count];

    public IReadOnlyList<int> Digits => _to;

    public void SetImmediate(IReadOnlyList<int> digits)
    {
        for (int i = 0; i < ClockDigits.Count; i++)
        {
            _from[i] = _to[i] = digits[i];
            _active[i] = false;
        }
    }

    /// <summary>
    /// Starts flipping every tile whose digit changes (or every tile when <paramref name="flipAll"/>),
    /// cascading right to left. Returns the time the first flap lands, or null if nothing flips.
    /// </summary>
    public double? Begin(IReadOnlyList<int> next, double nowMs, FlipTiming timing, bool flipAll = false)
    {
        int[] order = flipAll ? [5, 4, 3, 2, 1, 0] : ClockDigits.Changed(_to, next);
        if (order.Length == 0) return null;

        for (int k = 0; k < order.Length; k++)
        {
            int i = order[k];
            // A tile still flipping from the previous change snaps to its target first.
            _from[i] = _to[i];
            _to[i] = next[i];
            _start[i] = nowMs + k * timing.CascadeMs;
            _duration[i] = Math.Max(1, timing.DurationMs);
            _bounce[i] = timing.Bounce;
            _active[i] = true;
        }
        return nowMs + timing.DurationMs * FlapMotion.LandFraction(timing.Bounce);
    }

    public bool IsAnimating(double nowMs)
    {
        for (int i = 0; i < ClockDigits.Count; i++)
            if (_active[i] && nowMs < _start[i] + _duration[i]) return true;
        return false;
    }

    public TileVisual GetVisual(int i, double nowMs)
    {
        if (!_active[i] || nowMs >= _start[i] + _duration[i])
        {
            _active[i] = false;
            return new TileVisual(_to[i], _to[i], false, 0, _to[i], _to[i]);
        }
        if (nowMs < _start[i])
            return new TileVisual(_from[i], _from[i], false, 0, _from[i], _from[i]);

        double progress = (nowMs - _start[i]) / _duration[i];
        double angle = FlapMotion.Angle(progress, _bounce[i]);
        bool pastLanding = progress >= FlapMotion.LandFraction(_bounce[i]);
        // The old bottom half stays visible until the flap lands on it; during the rebound the new one shows.
        int bottom = pastLanding ? _to[i] : _from[i];
        return new TileVisual(_to[i], bottom, true, angle, _from[i], _to[i]);
    }
}
