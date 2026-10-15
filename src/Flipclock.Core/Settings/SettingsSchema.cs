using System.Globalization;

namespace Flipclock.Core.Settings;

/// <summary>
/// Maps every setting to a TOML section/key with its validation rule.
/// A value that is missing, of the wrong type, or out of range is rejected
/// and the setting keeps its default.
/// </summary>
internal static class SettingsSchema
{
    internal sealed record Entry(
        string Section,
        string Key,
        Func<ClockSettings, object?> Get,
        Func<ClockSettings, object, bool> TrySet);

    public static readonly IReadOnlyList<Entry> Entries =
    [
        Double("clock", "seconds_scale", s => s.SecondsScale, (s, v) => s.SecondsScale = v, 0.3, 1.0),
        Double("clock", "margin", s => s.Margin, (s, v) => s.Margin = v, 0, 500),

        Color("colors", "tile", s => s.TileColor, (s, v) => s.TileColor = v),
        Color("colors", "digit", s => s.DigitColor, (s, v) => s.DigitColor = v),
        Color("colors", "date", s => s.DateColor, (s, v) => s.DateColor = v),
        Color("colors", "background", s => s.BackgroundColor, (s, v) => s.BackgroundColor = v),

        Text("fonts", "digit_family", s => s.DigitFontFamily, (s, v) => s.DigitFontFamily = v, allowEmpty: false),
        Int("fonts", "digit_weight", s => s.DigitFontWeight, (s, v) => s.DigitFontWeight = v, 100, 950),
        Text("fonts", "date_family", s => s.DateFontFamily, (s, v) => s.DateFontFamily = v, allowEmpty: false),
        Int("fonts", "date_weight", s => s.DateFontWeight, (s, v) => s.DateFontWeight = v, 100, 950),

        Double("lighting", "angle", s => s.LightAngle, (s, v) => s.LightAngle = v, 0, 360),
        Double("lighting", "intensity", s => s.LightIntensity, (s, v) => s.LightIntensity = v, 0.2, 2.0),
        Double("lighting", "glossiness", s => s.Glossiness, (s, v) => s.Glossiness = v, 0, 1),

        Int("animation", "flip_duration_ms", s => s.FlipDurationMs, (s, v) => s.FlipDurationMs = v, 150, 1000),
        Int("animation", "cascade_delay_ms", s => s.CascadeDelayMs, (s, v) => s.CascadeDelayMs = v, 0, 200),
        Double("animation", "bounce", s => s.Bounce, (s, v) => s.Bounce = v, 0, 1),

        Bool("sound", "enabled", s => s.SoundEnabled, (s, v) => s.SoundEnabled = v),
        Double("sound", "volume", s => s.Volume, (s, v) => s.Volume = v, 0, 1),
        Text("sound", "file", s => s.SoundFile, (s, v) => s.SoundFile = v),

        Text("background", "image", s => s.BackgroundImage, (s, v) => s.BackgroundImage = v),
        Double("background", "dim", s => s.BackgroundDim, (s, v) => s.BackgroundDim = v, 0, 0.9),

        Bool("date", "visible", s => s.DateVisible, (s, v) => s.DateVisible = v),
        Enum<DatePosition>("date", "position", s => s.DatePosition, (s, v) => s.DatePosition = v),
        Double("date", "size", s => s.DateSize, (s, v) => s.DateSize = v, 0.5, 3.0),
        Enum<DateLanguage>("date", "language", s => s.DateLanguage, (s, v) => s.DateLanguage = v),

        OptionalInt("window", "x", s => s.WindowX, (s, v) => s.WindowX = v),
        OptionalInt("window", "y", s => s.WindowY, (s, v) => s.WindowY = v),
        Int("window", "width", s => s.WindowWidth, (s, v) => s.WindowWidth = v, 480, 16384),
        Int("window", "height", s => s.WindowHeight, (s, v) => s.WindowHeight = v, 270, 16384),
        Bool("window", "maximized", s => s.Maximized, (s, v) => s.Maximized = v),
        Bool("window", "fullscreen", s => s.Fullscreen, (s, v) => s.Fullscreen = v),
        Bool("window", "borderless", s => s.Borderless, (s, v) => s.Borderless = v),
        Bool("window", "always_on_top", s => s.AlwaysOnTop, (s, v) => s.AlwaysOnTop = v),
    ];

    static Entry Double(string section, string key, Func<ClockSettings, double> get, Action<ClockSettings, double> set, double min, double max) =>
        new(section, key, s => get(s), (s, raw) =>
        {
            double? v = raw switch { double d => d, long l => l, _ => null };
            if (v is not { } d2 || double.IsNaN(d2) || d2 < min || d2 > max) return false;
            set(s, d2);
            return true;
        });

    static Entry Int(string section, string key, Func<ClockSettings, int> get, Action<ClockSettings, int> set, int min, int max) =>
        new(section, key, s => get(s), (s, raw) =>
        {
            if (raw is not long l || l < min || l > max) return false;
            set(s, (int)l);
            return true;
        });

    static Entry OptionalInt(string section, string key, Func<ClockSettings, int?> get, Action<ClockSettings, int?> set) =>
        new(section, key, s => get(s), (s, raw) =>
        {
            if (raw is not long l || l < int.MinValue / 2 || l > int.MaxValue / 2) return false;
            set(s, (int)l);
            return true;
        });

    static Entry Bool(string section, string key, Func<ClockSettings, bool> get, Action<ClockSettings, bool> set) =>
        new(section, key, s => get(s), (s, raw) =>
        {
            if (raw is not bool b) return false;
            set(s, b);
            return true;
        });

    static Entry Text(string section, string key, Func<ClockSettings, string> get, Action<ClockSettings, string> set, bool allowEmpty = true) =>
        new(section, key, s => get(s), (s, raw) =>
        {
            if (raw is not string str || (!allowEmpty && string.IsNullOrWhiteSpace(str))) return false;
            set(s, str);
            return true;
        });

    static Entry Color(string section, string key, Func<ClockSettings, RgbColor> get, Action<ClockSettings, RgbColor> set) =>
        new(section, key, s => get(s).ToString(), (s, raw) =>
        {
            if (raw is not string str || !RgbColor.TryParse(str, out var c)) return false;
            set(s, c);
            return true;
        });

    static Entry Enum<T>(string section, string key, Func<ClockSettings, T> get, Action<ClockSettings, T> set) where T : struct, System.Enum =>
        new(section, key, s => get(s).ToString().ToLowerInvariant(), (s, raw) =>
        {
            if (raw is not string str || !System.Enum.TryParse<T>(str, ignoreCase: true, out var v) || !System.Enum.IsDefined(v))
                return false;
            set(s, v);
            return true;
        });

    public static string FormatValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => FormatDouble(d),
        string s => Quote(s),
        _ => throw new ArgumentException($"Unsupported value type {value.GetType()}"),
    };

    static string FormatDouble(double d)
    {
        var s = Math.Round(d, 4).ToString("0.0###", CultureInfo.InvariantCulture);
        return s;
    }

    static string Quote(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(ch)) sb.Append($"\\u{(int)ch:X4}");
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
