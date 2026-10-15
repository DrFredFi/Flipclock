using System.Globalization;

namespace Flipclock.Core.Settings;

/// <summary>An opaque sRGB color, serialized as <c>#RRGGBB</c>.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static RgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Invalid color '{text}'.");

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
