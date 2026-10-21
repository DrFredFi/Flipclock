namespace Flipclock.Core.Timers;

/// <summary>
/// All countdowns of the session. They run independently of which one is on screen; only one is
/// shown for now, but every one keeps counting and can finish in the background.
/// </summary>
public sealed class CountdownCollection
{
    readonly List<Countdown> _items = [];

    public IReadOnlyList<Countdown> Items => _items;

    public Countdown Add()
    {
        var countdown = new Countdown();
        _items.Add(countdown);
        return countdown;
    }

    /// <summary>Advances every countdown; returns the ones that finished on this call.</summary>
    public IReadOnlyList<Countdown> Update(double nowMs) => [.. _items.Where(c => c.Update(nowMs))];

    /// <summary>The earliest instant any running countdown changes its display, or null when none runs.</summary>
    public double? NextChangeMs(double nowMs)
    {
        double? next = null;
        foreach (var c in _items)
            if (c.NextChangeMs(nowMs) is { } at && (next is null || at < next)) next = at;
        return next;
    }
}
