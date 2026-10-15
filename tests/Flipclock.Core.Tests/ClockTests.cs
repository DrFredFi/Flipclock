using Flipclock.Core.Clock;
using Flipclock.Core.Settings;

namespace Flipclock.Core.Tests;

public class DateFormatterTests
{
    static readonly DateTime Thursday = new(2026, 10, 15);

    [Theory]
    [InlineData(DateLanguage.Italian, "Giovedì, 15 Ottobre 2026")]
    [InlineData(DateLanguage.English, "Thursday, 15 October 2026")]
    [InlineData(DateLanguage.Spanish, "Jueves, 15 Octubre 2026")]
    public void Formats_weekday_day_month_year(DateLanguage language, string expected) =>
        Assert.Equal(expected, DateFormatter.Format(Thursday, language));

    [Fact]
    public void Single_digit_day_has_no_padding() =>
        Assert.Equal("Domenica, 1 Febbraio 2026", DateFormatter.Format(new DateTime(2026, 2, 1), DateLanguage.Italian));
}

public class ClockDigitsTests
{
    [Fact]
    public void Splits_time_into_six_digits() =>
        Assert.Equal([0, 9, 5, 9, 0, 7], ClockDigits.FromTime(new TimeSpan(9, 59, 7)));

    [Fact]
    public void Changed_lists_rightmost_first()
    {
        var before = ClockDigits.FromTime(new TimeSpan(9, 59, 59));
        var after = ClockDigits.FromTime(new TimeSpan(10, 0, 0));
        Assert.Equal([5, 4, 3, 2, 1, 0], ClockDigits.Changed(before, after));
        Assert.Equal([5], ClockDigits.Changed(ClockDigits.FromTime(new TimeSpan(0, 0, 1)), ClockDigits.FromTime(new TimeSpan(0, 0, 2))));
    }
}

public class ClockLayoutTests
{
    [Fact]
    public void Clock_fits_inside_margins_and_is_centered()
    {
        var l = ClockLayout.Compute(1920, 1080, 250, 1.0, 1.0);
        Assert.Equal(250, l.MarginX);
        Assert.Equal(250, l.MarginY);
        Assert.True(l.Tiles[0].X >= 250 - 1e-9);
        Assert.True(l.Tiles[5].Right <= 1920 - 250 + 1e-9);
        Assert.True(l.Tiles[0].Y >= 250 - 1e-9 && l.Tiles[0].Bottom <= 1080 - 250 + 1e-9);
        Assert.Equal(960, (l.Tiles[0].X + l.Tiles[5].Right) / 2, 6);
    }

    [Fact]
    public void Margin_shrinks_to_keep_forty_percent()
    {
        var l = ClockLayout.Compute(600, 400, 250, 1.0, 1.0);
        Assert.Equal(180, l.MarginX, 6);
        Assert.Equal(120, l.MarginY, 6);
        Assert.True(l.Tiles[5].Right - l.Tiles[0].X <= 600 * 0.4 + 1e-6);
    }

    [Fact]
    public void Small_seconds_tiles_are_bottom_aligned()
    {
        var l = ClockLayout.Compute(1920, 1080, 100, 0.5, 1.0);
        Assert.Equal(l.Tiles[3].Height * 0.5, l.Tiles[4].Height, 6);
        Assert.Equal(l.Tiles[3].Bottom, l.Tiles[4].Bottom, 6);
        Assert.Equal(l.Tiles[3].Bottom, l.Tiles[5].Bottom, 6);
    }

    [Fact]
    public void Colons_keep_full_size_between_groups()
    {
        var l = ClockLayout.Compute(1920, 1080, 100, 0.4, 1.0);
        Assert.Equal(l.Colons[0].DotRadius, l.Colons[1].DotRadius);
        Assert.InRange(l.Colons[1].CenterX, l.Tiles[3].Right, l.Tiles[4].X);
    }

    [Fact]
    public void Layout_scales_linearly()
    {
        var a = ClockLayout.Compute(1000, 700, 100, 0.7, 1);
        var b = ClockLayout.Compute(2000, 1400, 200, 0.7, 1);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(a.Tiles[i].X * 2, b.Tiles[i].X, 6);
            Assert.Equal(a.Tiles[i].Height * 2, b.Tiles[i].Height, 6);
        }
    }
}

public class FlipAnimatorTests
{
    static readonly FlipTiming Timing = new(450, 60, 0.5);

    [Fact]
    public void Angle_runs_from_upright_to_landed()
    {
        Assert.Equal(0, FlapMotion.Angle(0, 0.5));
        Assert.Equal(180, FlapMotion.Angle(FlapMotion.LandFraction(0.5), 0.5), 6);
        Assert.Equal(180, FlapMotion.Angle(1, 0.5), 6);
        Assert.True(FlapMotion.Angle(FlapMotion.LandFraction(0.5) + 0.05, 0.5) < 180);
        Assert.Equal(180, FlapMotion.Angle(1, 0), 6);
    }

    [Fact]
    public void Flip_shows_new_top_and_old_bottom_until_landing()
    {
        var a = new FlipAnimator();
        a.SetImmediate([1, 2, 3, 4, 5, 8]);
        var land = a.Begin([1, 2, 3, 4, 5, 9], 1000, Timing);
        Assert.Equal(1000 + 450 * FlapMotion.LandFraction(0.5), land);

        var mid = a.GetVisual(5, 1100);
        Assert.True(mid.FlapVisible);
        Assert.Equal(9, mid.StaticTopDigit);
        Assert.Equal(8, mid.StaticBottomDigit);
        Assert.Equal(8, mid.FlapFrontDigit);
        Assert.Equal(9, mid.FlapBackDigit);

        Assert.Equal(9, a.GetVisual(5, land!.Value + 1).StaticBottomDigit);
        Assert.True(a.IsAnimating(1449));
        Assert.False(a.IsAnimating(1451));
        Assert.Equal(new TileVisual(9, 9, false, 0, 9, 9), a.GetVisual(5, 1451));
        Assert.False(a.GetVisual(4, 1100).FlapVisible);
    }

    [Fact]
    public void Cascade_delays_tiles_to_the_left()
    {
        var a = new FlipAnimator();
        a.SetImmediate([0, 9, 5, 9, 5, 9]);
        a.Begin([1, 0, 0, 0, 0, 0], 0, Timing);
        Assert.True(a.GetVisual(5, 10).FlapVisible);
        var waiting = a.GetVisual(0, 10);
        Assert.False(waiting.FlapVisible);
        Assert.Equal(0, waiting.StaticTopDigit);
        Assert.True(a.GetVisual(0, 5 * 60 + 10).FlapVisible);
        Assert.True(a.IsAnimating(5 * 60 + 449));
    }

    [Fact]
    public void Nothing_changed_means_no_flip_unless_forced()
    {
        var a = new FlipAnimator();
        a.SetImmediate([1, 2, 3, 4, 5, 6]);
        Assert.Null(a.Begin([1, 2, 3, 4, 5, 6], 0, Timing));
        Assert.NotNull(a.Begin([1, 2, 3, 4, 5, 6], 0, Timing, flipAll: true));
        Assert.True(a.GetVisual(0, 5 * 60 + 100).FlapVisible);
    }
}
