namespace Flipclock.Core.Clock;

/// <summary>How full the battery is, which picks the fill color: green, yellow or red.</summary>
public enum BatteryLevel { High, Medium, Low }

/// <summary>
/// Sizes of the battery indicator drawn in a window corner: the icon body (with a small cap on its
/// right), the stroke and inner padding of the outline, and the percentage text below it.
/// </summary>
public sealed record BatteryLayoutResult(
    double BodyWidth,
    double BodyHeight,
    double CapWidth,
    double CapHeight,
    double Stroke,
    double FontSize,
    double EdgePadding)
{
    /// <summary>Width available to the fill inside the outline and its padding.</summary>
    public double InnerWidth => Math.Max(0, BodyWidth - 4 * Stroke);
    public double InnerHeight => Math.Max(0, BodyHeight - 4 * Stroke);
}

public static class BatteryIndicator
{
    /// <summary>At or below this the fill turns red.</summary>
    public const int LowPercent = 20;
    /// <summary>At or below this the fill turns yellow.</summary>
    public const int MediumPercent = 50;

    public static BatteryLevel LevelFor(int percent) =>
        percent <= LowPercent ? BatteryLevel.Low : percent <= MediumPercent ? BatteryLevel.Medium : BatteryLevel.High;

    /// <summary>The share of the inner width filled for a charge percentage.</summary>
    public static double FillFraction(int percent) => Math.Clamp(percent, 0, 100) / 100.0;

    /// <summary>Scales with the window like the date does; <paramref name="size"/> is the user multiplier.</summary>
    public static BatteryLayoutResult Layout(double width, double height, double size)
    {
        double unit = Math.Max(20, 0.06 * Math.Min(Math.Max(width, 1), Math.Max(height, 1)) * size);
        double stroke = Math.Max(2, unit * 0.07);
        return new BatteryLayoutResult(
            BodyWidth: unit * 2,
            BodyHeight: unit,
            CapWidth: unit * 0.14,
            CapHeight: unit * 0.42,
            Stroke: stroke,
            // The percentage stays secondary to the icon but legible at a glance.
            FontSize: Math.Max(10, unit * 0.55),
            EdgePadding: Math.Max(16, Math.Min(width, height) * 0.03));
    }
}
