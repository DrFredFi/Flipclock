using Flipclock.Core.Clock;

namespace Flipclock.Core.Timers;

public enum CountdownState { Idle, Running, Paused, Finished }

/// <summary>
/// One countdown timer. Times are in milliseconds on any monotonic clock; the remaining time is
/// always derived from the end instant, so it never drifts however late the caller polls.
/// </summary>
public sealed class Countdown
{
    public static readonly TimeSpan MaxDuration = new(99, 59, 59);

    double _endMs;
    TimeSpan _remainingWhenPaused;

    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; set; } = "";
    /// <summary>The value set with the arrows; Reset returns to it.</summary>
    public TimeSpan Duration { get; private set; }
    public CountdownState State { get; private set; }

    /// <summary>Digits shown while idle, edited one tile at a time.</summary>
    public int[] SetDigits => ClockDigits.FromDuration(Duration);

    public void SetDuration(TimeSpan duration)
    {
        if (State != CountdownState.Idle) return;
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration > MaxDuration ? MaxDuration : duration;
    }

    /// <summary>Steps one tile up or down, wrapping within that tile's range without carrying.</summary>
    public void StepDigit(int index, int delta)
    {
        if (State != CountdownState.Idle) return;
        SetDuration(CountdownDigits.ToDuration(CountdownDigits.Step(SetDigits, index, delta)));
    }

    public bool CanStart => State == CountdownState.Idle && Duration > TimeSpan.Zero;

    public void Start(double nowMs)
    {
        if (!CanStart) return;
        _endMs = nowMs + Duration.TotalMilliseconds;
        State = CountdownState.Running;
    }

    public void Pause(double nowMs)
    {
        if (State != CountdownState.Running) return;
        _remainingWhenPaused = Remaining(nowMs);
        State = CountdownState.Paused;
    }

    public void Resume(double nowMs)
    {
        if (State != CountdownState.Paused) return;
        _endMs = nowMs + _remainingWhenPaused.TotalMilliseconds;
        State = CountdownState.Running;
    }

    /// <summary>Back to the value that was set, ready to start again.</summary>
    public void Reset() => State = CountdownState.Idle;

    public TimeSpan Remaining(double nowMs) => State switch
    {
        CountdownState.Idle => Duration,
        CountdownState.Paused => _remainingWhenPaused,
        CountdownState.Finished => TimeSpan.Zero,
        _ => TimeSpan.FromMilliseconds(Math.Max(0, _endMs - nowMs)),
    };

    /// <summary>The displayed digits. Seconds round up, so 00:00:00 appears exactly at the end.</summary>
    public int[] Digits(double nowMs)
    {
        var remaining = Remaining(nowMs);
        return ClockDigits.FromDuration(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds - 1e-6)));
    }

    /// <summary>Moves a running countdown to Finished once its time is up. Returns true on that transition.</summary>
    public bool Update(double nowMs)
    {
        if (State != CountdownState.Running || nowMs < _endMs) return false;
        State = CountdownState.Finished;
        return true;
    }

    /// <summary>When the displayed digits next change (or the countdown ends), or null if they are static.</summary>
    public double? NextChangeMs(double nowMs)
    {
        if (State != CountdownState.Running) return null;
        double left = _endMs - nowMs;
        if (left <= 0) return nowMs;
        double intoSecond = left % 1000;
        return nowMs + (intoSecond == 0 ? 1000 : intoSecond);
    }
}

/// <summary>Per-tile editing of countdown digits H H M M S S.</summary>
public static class CountdownDigits
{
    static readonly int[] Max = [9, 9, 5, 9, 5, 9];

    public static int[] Step(IReadOnlyList<int> digits, int index, int delta)
    {
        int[] next = [.. digits];
        int range = Max[index] + 1;
        next[index] = ((next[index] + delta) % range + range) % range;
        return next;
    }

    public static TimeSpan ToDuration(IReadOnlyList<int> d) =>
        new(d[0] * 10 + d[1], d[2] * 10 + d[3], d[4] * 10 + d[5]);
}
