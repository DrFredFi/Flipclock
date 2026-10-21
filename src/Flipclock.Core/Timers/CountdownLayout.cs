using Flipclock.Core.Clock;

namespace Flipclock.Core.Timers;

public sealed record CountdownLayoutResult(
    ClockLayoutResult Clock,
    double Margin,
    double ArrowSize,
    bool ArrowsFit,
    double TextFontSize,
    double DescriptionCenterY,
    double ControlsCenterY);

/// <summary>
/// The countdown screen: the clock with room above it for the description and the up arrows, and
/// below it for the down arrows and the controls. The margin grows past the user's setting when
/// needed; when even the largest margin is too thin (tiny windows) the arrows are dropped.
/// </summary>
public static class CountdownLayout
{
    /// <summary>Height of the Start / Pause / Reset buttons, in the caller's units.</summary>
    public const double ControlsHeight = 40;

    public static CountdownLayoutResult Compute(double width, double height, double margin, double secondsScale, double dateSize)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        double font = Math.Max(8, 0.035 * Math.Min(width, height) * dateSize);
        double arrow = Math.Clamp(0.05 * Math.Min(width, height), 12, 40);
        double arrowBand = arrow * 1.5;
        double controlsBand = ControlsHeight + 24;

        double need = Math.Max(font * 2 + arrowBand, controlsBand + arrowBand);
        double effective = Math.Max(margin, need);
        var clock = ClockLayout.Compute(width, height, effective, secondsScale, dateSize);

        double tilesTop = clock.Tiles[0].Y, tilesBottom = clock.Tiles[0].Bottom;
        bool fit = tilesTop >= need - 1e-6 && height - tilesBottom >= need - 1e-6 && clock.Tiles[5].Width >= arrow;
        double band = fit ? arrowBand : 0;
        double descriptionCenter = (tilesTop - band) / 2;
        double controlsCenter = (tilesBottom + band + height) / 2;

        return new CountdownLayoutResult(clock, effective, arrow, fit, font, descriptionCenter, controlsCenter);
    }
}
