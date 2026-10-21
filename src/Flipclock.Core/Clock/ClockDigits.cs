namespace Flipclock.Core.Clock;

/// <summary>The six displayed digits H H M M S S of a 24h time.</summary>
public static class ClockDigits
{
    public const int Count = 6;

    public static int[] FromTime(TimeSpan timeOfDay)
    {
        int h = timeOfDay.Hours, m = timeOfDay.Minutes, s = timeOfDay.Seconds;
        return [h / 10, h % 10, m / 10, m % 10, s / 10, s % 10];
    }

    public static int[] FromTime(DateTime time) => FromTime(time.TimeOfDay);

    /// <summary>A duration as HH MM SS, hours counting past 24 and capped at 99:59:59.</summary>
    public static int[] FromDuration(TimeSpan duration)
    {
        long total = Math.Clamp((long)duration.TotalSeconds, 0, 99 * 3600 + 59 * 60 + 59);
        int h = (int)(total / 3600), m = (int)(total / 60 % 60), s = (int)(total % 60);
        return [h / 10, h % 10, m / 10, m % 10, s / 10, s % 10];
    }

    /// <summary>Indices of tiles whose digit differs, rightmost first (the cascade order).</summary>
    public static int[] Changed(IReadOnlyList<int> previous, IReadOnlyList<int> next)
    {
        var changed = new List<int>(Count);
        for (int i = Count - 1; i >= 0; i--)
            if (previous[i] != next[i]) changed.Add(i);
        return [.. changed];
    }
}
