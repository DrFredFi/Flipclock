namespace Flipclock.Core.Settings;

public enum DatePosition { Left, Center, Right }

public enum DateLanguage { Italian, English, Spanish }

/// <summary>
/// Every user-customizable option. Defaults here are the factory defaults;
/// ranges are enforced by <see cref="SettingsSchema"/> when loading.
/// </summary>
public sealed class ClockSettings
{
    // [clock]
    public double SecondsScale { get; set; } = 1.0;
    public double Margin { get; set; } = 250;

    // [colors]
    public RgbColor TileColor { get; set; } = new(0x1C, 0x1C, 0x1E);
    public RgbColor DigitColor { get; set; } = new(0xF2, 0xF2, 0xF0);
    public RgbColor DateColor { get; set; } = new(0xF2, 0xF2, 0xF0);
    public RgbColor BackgroundColor { get; set; } = new(0x3A, 0x3F, 0x47);

    // [fonts]
    public string DigitFontFamily { get; set; } = "Segoe UI";
    public int DigitFontWeight { get; set; } = 700;
    public string DateFontFamily { get; set; } = "Segoe UI";
    public int DateFontWeight { get; set; } = 400;

    // [lighting]
    public double LightAngle { get; set; } = 135;
    public double LightIntensity { get; set; } = 1.0;
    public double Glossiness { get; set; } = 0.45;

    // [animation]
    public int FlipDurationMs { get; set; } = 450;
    public int CascadeDelayMs { get; set; } = 60;
    public double Bounce { get; set; } = 0.5;

    // [sound]
    public bool SoundEnabled { get; set; } = true;
    public double Volume { get; set; } = 0.6;
    public string SoundFile { get; set; } = "";

    // [background]
    public string BackgroundImage { get; set; } = "";
    public double BackgroundDim { get; set; } = 0.0;

    // [date]
    public bool DateVisible { get; set; } = true;
    public DatePosition DatePosition { get; set; } = DatePosition.Center;
    public double DateSize { get; set; } = 1.0;
    public DateLanguage DateLanguage { get; set; } = DateLanguage.Italian;

    // [window]
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool Maximized { get; set; }
    public bool Fullscreen { get; set; }
    public bool Borderless { get; set; }
    public bool AlwaysOnTop { get; set; }

    public ClockSettings Clone() => (ClockSettings)MemberwiseClone();

    /// <summary>Copies everything except window placement from <paramref name="other"/>.</summary>
    public void CopyAppearanceFrom(ClockSettings other)
    {
        var placement = (WindowX, WindowY, WindowWidth, WindowHeight, Maximized, Fullscreen, Borderless, AlwaysOnTop);
        foreach (var p in typeof(ClockSettings).GetProperties())
            p.SetValue(this, p.GetValue(other));
        (WindowX, WindowY, WindowWidth, WindowHeight, Maximized, Fullscreen, Borderless, AlwaysOnTop) = placement;
    }
}
