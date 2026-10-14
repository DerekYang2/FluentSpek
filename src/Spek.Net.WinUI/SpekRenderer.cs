using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Spek.Net.WinUI;

// What one frame of the view shows, apart from the spectrogram pixels.
internal sealed record SpekScene(
    string? Title,
    string Description,
    double Duration,
    int SampleRate,
    int LowerDb,
    int UpperDb,
    string? BrandName,
    string? BrandVersion,
    FrequencyRange? FrequencyRange = null,
    ReferenceLines ReferenceLines = ReferenceLines.None,
    SpectralDrop? Cutoff = null);

// Draws Spek's layout: the title and description above the plot, the time ruler below it, the frequency ruler on the
// left, and the palette with its dB ruler on the right. Sizes are in device-independent pixels.
internal static class SpekRenderer
{
    public const float LeftPad = 60;
    public const float TopPad = 60;
    public const float RightPad = 92;
    public const float BottomPad = 40;
    public const float Gap = 10;
    public const float PaletteWidth = 10;
    private const float TickLength = 4;
    private const string FontFamily = "Segoe UI";

    private static readonly Color Foreground = Colors.White;

    // Spek's 9, 10 and 8 point fonts at 96 DPI.
    private static readonly CanvasTextFormat NormalFont = Font(12, bold: false);
    private static readonly CanvasTextFormat NormalBoldFont = Font(12, bold: true);
    private static readonly CanvasTextFormat LargeFont = Font(13.333f, bold: true);
    private static readonly CanvasTextFormat SmallFont = Font(10.667f, bold: false);

    public static Rect PlotRect(Size size) =>
        new(LeftPad, TopPad, Math.Max(0, size.Width - LeftPad - RightPad), Math.Max(0, size.Height - TopPad - BottomPad));

    public static void Draw(
        CanvasDrawingSession ds,
        Size size,
        SpekScene scene,
        CanvasBitmap? spectrogram,
        int visibleColumns,
        CanvasBitmap palette,
        CanvasImageInterpolation interpolation)
    {
        float w = (float)size.Width, h = (float)size.Height;
        ds.Clear(Colors.Black);
        ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;

        float normalHeight = TextHeight(ds, NormalFont);
        float largeHeight = TextHeight(ds, LargeFont);
        float smallHeight = TextHeight(ds, SmallFont);

        if (!string.IsNullOrEmpty(scene.BrandName))
        {
            // Spek starts its name at the palette's left edge. A longer name moves left so the version still fits.
            float nameWidth = TextWidth(ds, scene.BrandName + " ", NormalBoldFont);
            float versionWidth = string.IsNullOrEmpty(scene.BrandVersion) ? 0 : TextWidth(ds, scene.BrandVersion, SmallFont);
            float brandX = Math.Min(w - RightPad + Gap, w - Gap - nameWidth - versionWidth);
            ds.DrawText(scene.BrandName, brandX, TopPad - 2 * Gap - 2 * normalHeight, Foreground, NormalBoldFont);
            if (!string.IsNullOrEmpty(scene.BrandVersion))
            {
                ds.DrawText(scene.BrandVersion, brandX + nameWidth, TopPad - 2 * Gap - normalHeight - smallHeight, Foreground, SmallFont);
            }
        }

        var plot = PlotRect(size);
        float plotW = (float)plot.Width, plotH = (float)plot.Height;
        var range = scene.FrequencyRange ?? new FrequencyRange(0, Math.Max(1, scene.SampleRate / 2.0));
        if (plotW > 0 && plotH > 0)
        {
            if (spectrogram is not null && visibleColumns > 0)
            {
                var bounds = spectrogram.SizeInPixels;
                int columns = (int)Math.Min(visibleColumns, bounds.Width);
                double nyquist = Math.Max(1, scene.SampleRate / 2.0);
                var source = new Rect(0, (nyquist - range.Maximum) / nyquist * bounds.Height,
                    columns, range.Span / nyquist * bounds.Height);
                var destination = new Rect(LeftPad, TopPad, plotW * columns / bounds.Width, plotH);
                ds.DrawImage(spectrogram, destination, source, 1, interpolation);
            }

            if (scene.SampleRate > 0) DrawGuides(ds, plot, range, scene.ReferenceLines, scene.Cutoff);

            if (!string.IsNullOrEmpty(scene.Title))
            {
                ds.DrawText(Trim(ds, scene.Title, plotW, LargeFont, trimEnd: false), LeftPad, TopPad - 2 * Gap - normalHeight - largeHeight, Foreground, LargeFont);
            }
            if (scene.Description.Length > 0)
            {
                ds.DrawText(Trim(ds, scene.Description, plotW, NormalFont, trimEnd: true), LeftPad, TopPad - Gap - normalHeight, Foreground, NormalFont);
            }

            if (scene.Duration > 0)
            {
                float label = TextWidth(ds, "00:00", SmallFont);
                var ticks = Ruler.Ticks(0, (int)scene.Duration, Ruler.TimeSteps, 1.5, plotW / scene.Duration, 0, label, vertical: false);
                DrawRuler(ds, ticks, LeftPad, h - BottomPad, RulerSide.Bottom, Ruler.FormatTime);
            }

            if (scene.SampleRate > 0)
            {
                int[] steps = [10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000];
                var ticks = Ruler.Ticks((int)range.Minimum, (int)range.Maximum, steps, 3.0, plotH / range.Span, 0, smallHeight, vertical: true);
                DrawRuler(ds, ticks, LeftPad, TopPad, RulerSide.Left, Ruler.FormatFrequency);
            }
        }

        ds.Antialiasing = CanvasAntialiasing.Aliased;
        if (plotW > 0 && plotH > 0) ds.DrawRectangle(LeftPad + 0.5f, TopPad + 0.5f, plotW - 1, plotH - 1, Foreground);

        if (plotH > 0)
        {
            var paletteSize = palette.SizeInPixels;
            ds.DrawImage(palette, new Rect(w - RightPad + Gap, TopPad, PaletteWidth, plotH + 1),
                new Rect(0, 0, paletteSize.Width, paletteSize.Height), 1, CanvasImageInterpolation.Linear);

            var ticks = Ruler.Ticks(-scene.UpperDb, -scene.LowerDb, Ruler.DensitySteps, 3.0,
                plotH / (double)(scene.LowerDb - scene.UpperDb), plotH, smallHeight, vertical: true);
            DrawRuler(ds, ticks, w - RightPad + Gap + PaletteWidth, TopPad, RulerSide.Right, Ruler.FormatDensity);
        }
        ds.Antialiasing = CanvasAntialiasing.Antialiased;
    }

    private static void DrawGuides(CanvasDrawingSession ds, Rect plot, FrequencyRange range, ReferenceLines mode, SpectralDrop? cutoff)
    {
        using var clip = ds.CreateLayer(1, plot);
        using var dots = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dot };
        if (mode == ReferenceLines.Mp3Lame)
        {
            var labels = new List<(float Y, string Text)>();
            foreach (var reference in EncoderReferences.Mp3Lame.Reverse())
            {
                if (reference.FrequencyHz < range.Minimum || reference.FrequencyHz > range.Maximum) continue;
                float y = (float)(plot.Top + range.FractionFromTop(reference.FrequencyHz) * plot.Height);
                ds.DrawLine((float)plot.Left, y, (float)plot.Right, y, Colors.Black, 3);
                ds.DrawLine((float)plot.Left, y, (float)plot.Right, y, Foreground, 1, dots);

                labels.Add((y, $"LAME {reference.BitRateKbps} ~{reference.FrequencyHz / 1000.0:0.0} kHz"));
            }
            float nextLabelY = (float)plot.Top + 2;
            for (int i = 0; i < labels.Count; i++)
            {
                var (y, label) = labels[i];
                float lastStart = (float)plot.Bottom - (labels.Count - i) * 16;
                float labelY = Math.Max(nextLabelY, Math.Min(y - 14, lastStart));
                if (labelY + 14 > plot.Bottom) break;
                float width = TextWidth(ds, label, SmallFont) + 8;
                float x = (float)plot.Right - width - 4;
                ds.FillRectangle(x, labelY, width, 14, Color.FromArgb(220, 0, 0, 0));
                ds.DrawText(label, x + 4, labelY, Foreground, SmallFont);
                nextLabelY = labelY + 16;
            }
        }

        if (cutoff is not null && cutoff.FrequencyHz >= range.Minimum && cutoff.FrequencyHz <= range.Maximum)
        {
            float y = (float)(plot.Top + range.FractionFromTop(cutoff.FrequencyHz) * plot.Height);
            ds.DrawLine((float)plot.Left, y, (float)plot.Right, y, Colors.Black, 4);
            ds.DrawLine((float)plot.Left, y, (float)plot.Right, y, Foreground, 2);
            string label = $"Measured drop ~{cutoff.FrequencyHz / 1000:0.00} kHz";
            float width = TextWidth(ds, label, SmallFont) + 8;
            float labelY = Math.Clamp(y + 2, (float)plot.Top, (float)Math.Max(plot.Top, plot.Bottom - 16));
            ds.FillRectangle((float)plot.Left + 4, labelY, width, 16, Color.FromArgb(220, 0, 0, 0));
            ds.DrawText(label, (float)plot.Left + 8, labelY, Foreground, SmallFont);
        }
    }

    // A one-pixel-wide image of the palette, from the top of the range at row 0 down to the bottom.
    public static CanvasBitmap CreatePaletteBitmap(ICanvasResourceCreator creator, Palette palette)
    {
        const int rows = 512;
        var bytes = new byte[rows * 4];
        for (int row = 0; row < rows; row++)
        {
            uint colour = Palettes.Map(palette, (rows - 1 - row) / (double)rows);
            bytes[row * 4] = (byte)colour;
            bytes[row * 4 + 1] = (byte)(colour >> 8);
            bytes[row * 4 + 2] = (byte)(colour >> 16);
            bytes[row * 4 + 3] = 255;
        }
        return CanvasBitmap.CreateFromBytes(creator, bytes, 1, rows, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
    }

    public static CanvasBitmap CreateSpectrogramBitmap(ICanvasResourceCreator creator, Spectrogram spectrogram) =>
        CanvasBitmap.CreateFromBytes(creator, new byte[spectrogram.Columns * spectrogram.Bins * 4], spectrogram.Columns, spectrogram.Bins,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);

    // Copies the columns that changed since the last upload into the bitmap.
    public static void Upload(Spectrogram spectrogram, CanvasBitmap bitmap)
    {
        if (spectrogram.TakeDirty(out int left, out int width, out var region))
        {
            bitmap.SetPixelBytes(region, left, 0, width, spectrogram.Bins);
        }
    }

    private enum RulerSide
    {
        Bottom,
        Left,
        Right,
    }

    private static void DrawRuler(CanvasDrawingSession ds, List<RulerTick> ticks, float x, float y, RulerSide side, Func<int, string> format)
    {
        foreach (var tick in ticks)
        {
            string label = format(tick.Value);
            var bounds = Measure(ds, label, SmallFont);
            float p = (float)tick.Position;
            float tw = (float)bounds.Width, th = (float)bounds.Height;
            switch (side)
            {
                case RulerSide.Bottom:
                    ds.DrawText(label, MathF.Round(x + p - tw / 2), y + Gap, Foreground, SmallFont);
                    ds.DrawLine(new Vector2(MathF.Floor(x + p) + 0.5f, y), new Vector2(MathF.Floor(x + p) + 0.5f, y + TickLength), Foreground);
                    break;
                case RulerSide.Left:
                    ds.DrawText(label, x - tw - Gap, MathF.Round(y + p - th / 2), Foreground, SmallFont);
                    ds.DrawLine(new Vector2(x, MathF.Floor(y + p) + 0.5f), new Vector2(x - TickLength, MathF.Floor(y + p) + 0.5f), Foreground);
                    break;
                case RulerSide.Right:
                    ds.DrawText(label, x + Gap, MathF.Round(y + p - th / 2), Foreground, SmallFont);
                    ds.DrawLine(new Vector2(x, MathF.Floor(y + p) + 0.5f), new Vector2(x + TickLength, MathF.Floor(y + p) + 0.5f), Foreground);
                    break;
            }
        }
    }

    // Shortens text to fit the width, replacing the start or the end with "...", like Spek.
    private static string Trim(CanvasDrawingSession ds, string text, float width, CanvasTextFormat format, bool trimEnd)
    {
        if (width <= 0) return "";
        if (TextWidth(ds, text, format) <= width) return text;

        const string ellipsis = "...";
        int i = 0, k = text.Length;
        while (k - i > 1)
        {
            int j = (i + k) / 2;
            string candidate = trimEnd ? text[..j] + ellipsis : ellipsis + text[j..];
            bool fits = TextWidth(ds, candidate, format) <= width;
            if (trimEnd == fits) i = j;
            else k = j;
        }
        return trimEnd ? text[..i] + ellipsis : ellipsis + text[k..];
    }

    private static Rect Measure(ICanvasResourceCreator creator, string text, CanvasTextFormat format)
    {
        using var layout = new CanvasTextLayout(creator, text, format, float.MaxValue, float.MaxValue);
        return layout.LayoutBoundsIncludingTrailingWhitespace;
    }

    private static float TextWidth(ICanvasResourceCreator creator, string text, CanvasTextFormat format) =>
        (float)Measure(creator, text, format).Width;

    private static float TextHeight(ICanvasResourceCreator creator, CanvasTextFormat format) =>
        (float)Measure(creator, "dummy", format).Height;

    private static CanvasTextFormat Font(float size, bool bold) => new()
    {
        FontFamily = FontFamily,
        FontSize = size,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };
}
