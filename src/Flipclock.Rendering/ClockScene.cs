using Flipclock.Core.Clock;
using Flipclock.Core.Settings;

namespace Flipclock.Rendering;

/// <summary>Everything the renderer needs to draw one frame.</summary>
public sealed class ClockScene
{
    /// <summary>Margin in pixels (already multiplied by the DPI scale).</summary>
    public double MarginPx { get; set; } = 250;
    public double SecondsScale { get; set; } = 1;
    public RgbColor TileColor { get; set; }
    public RgbColor DigitColor { get; set; }
    public RgbColor BackgroundColor { get; set; }
    public double Glossiness { get; set; } = 0.45;
    public double LightAngle { get; set; } = 135;
    public double LightIntensity { get; set; } = 1;
    public double BackgroundDim { get; set; }
    public TileVisual[] Tiles { get; } = new TileVisual[ClockDigits.Count];

    public void ApplySettings(ClockSettings s, double dpiScale)
    {
        MarginPx = s.Margin * dpiScale;
        SecondsScale = s.SecondsScale;
        TileColor = s.TileColor;
        DigitColor = s.DigitColor;
        BackgroundColor = s.BackgroundColor;
        Glossiness = s.Glossiness;
        LightAngle = s.LightAngle;
        LightIntensity = s.LightIntensity;
        BackgroundDim = s.BackgroundDim;
    }
}
