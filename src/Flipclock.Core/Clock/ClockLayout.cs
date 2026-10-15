namespace Flipclock.Core.Clock;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>A colon separator: two dots stacked vertically.</summary>
public readonly record struct ColonLayout(double CenterX, double TopDotY, double BottomDotY, double DotRadius);

public sealed record ClockLayoutResult(
    IReadOnlyList<RectD> Tiles,
    IReadOnlyList<ColonLayout> Colons,
    RectD ClockArea,
    double MarginX,
    double MarginY,
    double DateFontSize,
    double DateCenterY,
    double DateSidePadding);

/// <summary>
/// Positions the six tiles and two colons inside a window. Units are whatever the
/// caller passes (DIPs or pixels); the result scales linearly with the inputs.
/// </summary>
public static class ClockLayout
{
    /// <summary>Tile width / tile height.</summary>
    public const double TileAspect = 0.68;
    /// <summary>Gap between the two digits of a pair, in tile widths.</summary>
    public const double PairGap = 0.07;
    /// <summary>Horizontal slot of a colon, in big-tile widths.</summary>
    public const double ColonSlot = 0.42;
    /// <summary>Colon dot radius, in big-tile widths.</summary>
    public const double DotRadius = 0.075;
    /// <summary>The clock area never shrinks below this fraction of the window.</summary>
    public const double MinClockFraction = 0.4;

    public static ClockLayoutResult Compute(double width, double height, double margin, double secondsScale, double dateSize)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        secondsScale = Math.Clamp(secondsScale, 0.3, 1.0);

        // Margin shrinks so the clock keeps at least 40% of each dimension.
        double maxMarginFraction = (1 - MinClockFraction) / 2;
        double mx = Math.Min(margin, width * maxMarginFraction);
        double my = Math.Min(margin, height * maxMarginFraction);
        var area = new RectD(mx, my, width - 2 * mx, height - 2 * my);

        double unitsWide = 4 + 2 * PairGap + 2 * ColonSlot + secondsScale * (2 + PairGap);
        double unitsHigh = 1 / TileAspect;
        double w = Math.Min(area.Width / unitsWide, area.Height / unitsHigh);
        double h = w / TileAspect;

        double x = area.CenterX - unitsWide * w / 2;
        double top = area.CenterY - h / 2;
        double bottom = top + h;

        var tiles = new RectD[ClockDigits.Count];
        var colons = new ColonLayout[2];
        double gap = PairGap * w;

        tiles[0] = new RectD(x, top, w, h); x += w + gap;
        tiles[1] = new RectD(x, top, w, h); x += w;
        colons[0] = Colon(x, w, top, h); x += ColonSlot * w;
        tiles[2] = new RectD(x, top, w, h); x += w + gap;
        tiles[3] = new RectD(x, top, w, h); x += w;
        colons[1] = Colon(x, w, top, h); x += ColonSlot * w;

        double sw = w * secondsScale, sh = h * secondsScale, sgap = gap * secondsScale;
        tiles[4] = new RectD(x, bottom - sh, sw, sh); x += sw + sgap;
        tiles[5] = new RectD(x, bottom - sh, sw, sh);

        double dateFont = Math.Max(8, 0.035 * Math.Min(width, height) * dateSize);
        // The date sits centered in the bottom margin band; when the band is too thin it hugs the bottom edge.
        double band = Math.Max(my, dateFont * 2.2);
        double dateCenterY = height - band / 2;
        double sidePadding = Math.Max(16, width * 0.03);

        return new ClockLayoutResult(tiles, colons, area, mx, my, dateFont, dateCenterY, sidePadding);
    }

    static ColonLayout Colon(double slotX, double w, double top, double h) =>
        new(slotX + ColonSlot * w / 2, top + h * 0.36, top + h * 0.64, DotRadius * w);
}
