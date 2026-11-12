using Flipclock.Core.Clock;

namespace Flipclock.Core.Tests;

public class BatteryIndicatorTests
{
    [Theory]
    [InlineData(100, BatteryLevel.High)]
    [InlineData(51, BatteryLevel.High)]
    [InlineData(50, BatteryLevel.Medium)]
    [InlineData(21, BatteryLevel.Medium)]
    [InlineData(20, BatteryLevel.Low)]
    [InlineData(0, BatteryLevel.Low)]
    public void Level_follows_the_green_yellow_red_thresholds(int percent, BatteryLevel expected) =>
        Assert.Equal(expected, BatteryIndicator.LevelFor(percent));

    [Theory]
    [InlineData(-5, 0.0)]
    [InlineData(0, 0.0)]
    [InlineData(37, 0.37)]
    [InlineData(100, 1.0)]
    [InlineData(140, 1.0)]
    public void Fill_tracks_the_charge_and_stays_in_range(int percent, double expected) =>
        Assert.Equal(expected, BatteryIndicator.FillFraction(percent), 6);

    [Fact]
    public void Layout_scales_with_the_window_and_the_size_setting()
    {
        var small = BatteryIndicator.Layout(1280, 800, 1.0);
        var big = BatteryIndicator.Layout(2560, 1600, 1.0);
        var doubled = BatteryIndicator.Layout(1280, 800, 2.0);

        Assert.Equal(small.BodyWidth * 2, big.BodyWidth, 6);
        Assert.Equal(small.BodyWidth * 2, doubled.BodyWidth, 6);
        Assert.True(small.BodyWidth > small.BodyHeight);
        Assert.True(small.InnerWidth > 0 && small.InnerHeight > 0);
    }

    [Fact]
    public void Layout_never_collapses_in_a_tiny_window()
    {
        var tiny = BatteryIndicator.Layout(10, 10, 0.5);
        Assert.True(tiny.BodyHeight >= 20);
        Assert.True(tiny.FontSize >= 10);
        Assert.True(tiny.InnerWidth > 0);
    }
}
