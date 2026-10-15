using Flipclock.Core.Clock;
using SkiaSharp;

namespace Flipclock.Rendering;

/// <summary>Renders the digits 0–9 as coverage masks sized for a full tile face.</summary>
public static class GlyphRasterizer
{
    public const int Width = 512;
    public static readonly int Height = (int)Math.Round(Width / ClockLayout.TileAspect);

    /// <summary>Returns ten Alpha8 images of <see cref="Width"/>×<see cref="Height"/> bytes each.</summary>
    public static byte[][] RenderDigits(string fontFamily, int weight)
    {
        using var typeface = SKTypeface.FromFamilyName(fontFamily, new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
                             ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 100) { Edging = SKFontEdging.Antialias, Subpixel = true };

        // Size the font so the digits fill ~64% of the tile height and never exceed ~84% of its width.
        var all = Bounds(font, "0123456789");
        float widest = 0;
        for (int d = 0; d <= 9; d++) widest = Math.Max(widest, Bounds(font, d.ToString()).Width);
        float scale = Math.Min(Height * 0.64f / Math.Max(all.Height, 1), Width * 0.84f / Math.Max(widest, 1));
        font.Size = 100 * scale;
        all = Bounds(font, "0123456789");
        float baseline = Height / 2f - all.MidY;

        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var images = new byte[10][];
        for (int d = 0; d <= 9; d++)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                var text = d.ToString();
                var b = Bounds(font, text);
                canvas.DrawText(text, Width / 2f - b.MidX, baseline, SKTextAlign.Left, font, paint);
            }
            images[d] = CopyTight(bitmap);
        }
        return images;
    }

    static SKRect Bounds(SKFont font, string text)
    {
        font.MeasureText(text, out var bounds);
        return bounds;
    }

    static byte[] CopyTight(SKBitmap bitmap)
    {
        var src = bitmap.GetPixelSpan();
        var dst = new byte[Width * Height];
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < Height; y++)
            src.Slice(y * rowBytes, Width).CopyTo(dst.AsSpan(y * Width, Width));
        return dst;
    }

    /// <summary>Decodes an image file to tightly packed RGBA8, downscaled to at most 4096 px on its long side.</summary>
    public static (byte[] Pixels, int Width, int Height)? DecodeImage(string path)
    {
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded is null) return null;
            int w = decoded.Width, h = decoded.Height;
            float s = Math.Min(1f, 4096f / Math.Max(w, h));
            w = Math.Max(1, (int)(w * s));
            h = Math.Max(1, (int)(h * s));
            using var rgba = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(rgba))
            using (var image = SKImage.FromBitmap(decoded))
            {
                canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }
            var pixels = new byte[w * h * 4];
            var src = rgba.GetPixelSpan();
            for (int y = 0; y < h; y++)
                src.Slice(y * rgba.RowBytes, w * 4).CopyTo(pixels.AsSpan(y * w * 4, w * 4));
            return (pixels, w, h);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Installed font family names, sorted.</summary>
    public static IReadOnlyList<string> SystemFontFamilies() =>
        SKFontManager.Default.GetFontFamilies().Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
}
