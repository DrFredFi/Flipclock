using Flipclock.Core.Clock;
using Flipclock.Core.Timers;

namespace Flipclock.Core.Tests;

public class CountdownTests
{
    static Countdown Set(TimeSpan duration)
    {
        var c = new Countdown();
        c.SetDuration(duration);
        return c;
    }

    [Fact]
    public void Starts_idle_at_zero_and_cannot_start()
    {
        var c = new Countdown();
        Assert.Equal(CountdownState.Idle, c.State);
        Assert.Equal([0, 0, 0, 0, 0, 0], c.Digits(0));
        Assert.False(c.CanStart);
        c.Start(0);
        Assert.Equal(CountdownState.Idle, c.State);
    }

    [Fact]
    public void Steps_wrap_per_tile_without_carry()
    {
        var c = new Countdown();
        c.StepDigit(5, -1);
        Assert.Equal([0, 0, 0, 0, 0, 9], c.SetDigits);
        c.StepDigit(4, -1);
        Assert.Equal([0, 0, 0, 0, 5, 9], c.SetDigits);
        c.StepDigit(4, 1);
        Assert.Equal([0, 0, 0, 0, 0, 9], c.SetDigits);
        c.StepDigit(0, 1);
        Assert.Equal(TimeSpan.FromHours(10) + TimeSpan.FromSeconds(9), c.Duration);
    }

    [Fact]
    public void Counts_down_rounding_seconds_up()
    {
        var c = Set(TimeSpan.FromSeconds(90));
        c.Start(1000);
        Assert.Equal([0, 0, 0, 1, 3, 0], c.Digits(1000));
        Assert.Equal([0, 0, 0, 1, 3, 0], c.Digits(1999));
        Assert.Equal([0, 0, 0, 1, 2, 9], c.Digits(2001));
        Assert.Equal([0, 0, 0, 0, 0, 1], c.Digits(90_500));
        Assert.Equal([0, 0, 0, 0, 0, 0], c.Digits(91_000));
    }

    [Fact]
    public void Finishes_once_at_the_end()
    {
        var c = Set(TimeSpan.FromSeconds(2));
        c.Start(0);
        Assert.False(c.Update(1999));
        Assert.True(c.Update(2000));
        Assert.False(c.Update(2500));
        Assert.Equal(CountdownState.Finished, c.State);
        Assert.Equal([0, 0, 0, 0, 0, 0], c.Digits(3000));
    }

    [Fact]
    public void Pause_freezes_and_resume_continues()
    {
        var c = Set(TimeSpan.FromSeconds(10));
        c.Start(0);
        c.Pause(3500);
        Assert.Equal(TimeSpan.FromMilliseconds(6500), c.Remaining(100_000));
        Assert.Null(c.NextChangeMs(100_000));
        c.Resume(100_000);
        Assert.Equal(TimeSpan.FromMilliseconds(6500), c.Remaining(100_000));
        Assert.True(c.Update(106_500));
    }

    [Fact]
    public void Reset_returns_to_the_set_value_not_zero()
    {
        var c = Set(TimeSpan.FromMinutes(5));
        c.Start(0);
        c.Pause(60_000);
        c.Reset();
        Assert.Equal(CountdownState.Idle, c.State);
        Assert.Equal([0, 0, 0, 5, 0, 0], c.Digits(0));
        Assert.True(c.CanStart);
    }

    [Fact]
    public void Value_cannot_change_while_running()
    {
        var c = Set(TimeSpan.FromSeconds(10));
        c.Start(0);
        c.StepDigit(5, 1);
        c.SetDuration(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(10), c.Duration);
    }

    [Fact]
    public void Next_change_lands_on_display_boundaries()
    {
        var c = Set(TimeSpan.FromSeconds(10));
        c.Start(250);
        Assert.Equal(1250, c.NextChangeMs(250));
        Assert.Equal(1250, c.NextChangeMs(700));
        Assert.Equal(10_250, c.NextChangeMs(10_250));
    }

    [Fact]
    public void Collection_tracks_the_earliest_change_and_reports_finishers()
    {
        var all = new CountdownCollection();
        var a = all.Add();
        var b = all.Add();
        a.SetDuration(TimeSpan.FromSeconds(1));
        b.SetDuration(TimeSpan.FromSeconds(5));
        Assert.Null(all.NextChangeMs(0));
        a.Start(0);
        b.Start(300);
        Assert.Equal(300, all.NextChangeMs(0));
        Assert.Equal([a], all.Update(1000));
        Assert.Empty(all.Update(1001));
        Assert.Equal(1300, all.NextChangeMs(1001));
    }

    [Fact]
    public void Durations_past_a_day_keep_their_hours() =>
        Assert.Equal([9, 9, 5, 9, 5, 9], ClockDigits.FromDuration(TimeSpan.FromHours(200)));
}

public class CountdownLayoutTests
{
    [Fact]
    public void Arrows_get_room_even_with_zero_margin()
    {
        var l = CountdownLayout.Compute(1920, 1080, 0, 1.0, 1.0);
        Assert.True(l.ArrowsFit);
        Assert.True(l.Margin > 0);
        Assert.True(l.Clock.Tiles[0].Y >= l.ArrowSize * 1.5);
        Assert.True(l.DescriptionCenterY < l.Clock.Tiles[0].Y - l.ArrowSize * 1.5);
        Assert.True(l.ControlsCenterY - CountdownLayout.ControlsHeight / 2 > l.Clock.Tiles[0].Bottom + l.ArrowSize * 1.5);
    }

    [Fact]
    public void Larger_user_margin_is_kept()
    {
        var l = CountdownLayout.Compute(1920, 1080, 300, 1.0, 1.0);
        Assert.Equal(300, l.Margin);
        Assert.True(l.ArrowsFit);
    }

    [Fact]
    public void Arrows_are_dropped_when_the_window_is_too_short() =>
        Assert.False(CountdownLayout.Compute(1600, 270, 0, 1.0, 1.0).ArrowsFit);

    [Fact]
    public void Arrows_are_dropped_when_wider_than_the_seconds_tiles() =>
        Assert.False(CountdownLayout.Compute(480, 1000, 0, 0.3, 1.0).ArrowsFit);
}
