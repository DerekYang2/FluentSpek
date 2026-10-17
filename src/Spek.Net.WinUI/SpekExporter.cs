using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace Spek.Net.WinUI;

public sealed record ExportOptions(
    AnalysisSettings Analysis,
    Palette Palette = Palettes.Default,
    int LowerDb = SpekDefaults.LowerDb,
    int UpperDb = SpekDefaults.UpperDb,
    bool ShowFullPath = true,
    bool ShowDetailedDescription = false,
    string? BrandName = null,
    string? BrandVersion = null,
    CanvasImageInterpolation Interpolation = CanvasImageInterpolation.HighQualityCubic,
    ReferenceLines ReferenceLines = ReferenceLines.None,
    bool ShowCutoffMarker = false,
    FrequencyRange? FrequencyRange = null,
    TimeRange? TimeRange = null);

// Analyses a file and saves the whole view as a PNG without showing a window, like "spek FILE PNG WIDTH HEIGHT".
public static class SpekExporter
{
    public static async Task<AudioFileInfo> ExportAsync(
        Ffmpeg ffmpeg, string file, string pngPath, int width, int height, ExportOptions options, CancellationToken cancellationToken = default)
    {
        var info = await ffmpeg.ProbeAsync(file, cancellationToken).ConfigureAwait(false);
        var size = new Size(width, height);
        int columns = (int)SpekRenderer.PlotRect(size).Width;
        var settings = options.Analysis;
        var error = AudioError.None;

        Spectrogram? spectrogram = null;
        if (info.ErrorFor(settings.Stream) == AudioError.None && columns > 0)
        {
            spectrogram = new Spectrogram(columns, SpekDefaults.BinsFor(settings.FftBits), options.Palette, options.LowerDb, options.UpperDb);
            try
            {
                await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, spectrogram, cancellationToken).ConfigureAwait(false);
            }
            catch (FfmpegException)
            {
                error = AudioError.DecodingFailed;
            }
        }

        SpectralDrop? cutoff = null;
        if (options.ShowCutoffMarker && spectrogram is not null)
            cutoff = spectrogram.GetProfile(0, spectrogram.Columns, info.GetStream(settings.Stream)!.SampleRate, cancellationToken).FindDrop();
        var range = options.FrequencyRange;
        double nyquist = info.GetStream(settings.Stream)?.SampleRate / 2.0 ?? 0;
        if (range is not null && (range.Value.Span <= 0 || range.Value.Maximum > nyquist))
            throw new ArgumentOutOfRangeException(nameof(options), "The frequency range must fit within the file's Nyquist frequency.");
        if (options.TimeRange is { } time && (time.Span <= 0 || time.End > info.Duration))
            throw new ArgumentOutOfRangeException(nameof(options), "The time range must fit within the file's duration.");

        var scene = new SpekScene(
            options.ShowFullPath ? file : Path.GetFileName(file),
            SpekDescription.Describe(info, settings, options.ShowDetailedDescription, error),
            info.Duration,
            info.GetStream(settings.Stream)?.SampleRate ?? 0,
            options.LowerDb,
            options.UpperDb,
            options.BrandName,
            options.BrandVersion,
            range,
            options.ReferenceLines,
            cutoff,
            options.TimeRange);

        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, width, height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            using var palette = SpekRenderer.CreatePaletteBitmap(device, options.Palette);
            using var bitmap = spectrogram is null ? null : SpekRenderer.CreateSpectrogramBitmap(device, spectrogram);
            bitmap?.SetPixelBytes(spectrogram!.CopyPixels());
            SpekRenderer.Draw(ds, size, scene, bitmap, spectrogram?.Columns ?? 0, palette, options.Interpolation);
        }
        await target.SaveAsync(pngPath, CanvasBitmapFileFormat.Png).AsTask(cancellationToken).ConfigureAwait(false);
        return info;
    }
}
