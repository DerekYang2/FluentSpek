namespace Spek.Net;

public sealed record AnalysisSettings(
    int Stream = 0,
    int Channel = 0,
    int FftBits = SpekDefaults.FftBits,
    WindowFunction WindowFunction = WindowFunction.Hann);

public static class SpekAnalyzer
{
    // Decodes the chosen stream and channel and writes each finished column into the spectrogram. The spectrogram's
    // column count is the image width, and its bin count must match the FFT size in the settings.
    public static Task AnalyzeAsync(
        Ffmpeg ffmpeg,
        AudioFileInfo info,
        AnalysisSettings settings,
        Spectrogram target,
        CancellationToken cancellationToken = default)
    {
        if (info.ErrorFor(settings.Stream) is var error and not AudioError.None)
            throw new ArgumentException($"The file can't be analysed: {SpekDescription.ErrorText(error)}.", nameof(info));
        if (target.Bins != SpekDefaults.BinsFor(settings.FftBits))
            throw new ArgumentException("The spectrogram's bin count doesn't match the FFT size.", nameof(target));

        return Task.Run(async () =>
        {
            var pipeline = new SpectrogramPipeline(
                settings.FftBits, settings.WindowFunction, target.Columns, info.FrameCount(settings.Stream), target.SetColumn);
            await ffmpeg.DecodeAsync(info.Path, settings.Stream, settings.Channel, pipeline.Push, cancellationToken)
                .ConfigureAwait(false);
            pipeline.Complete();
        }, cancellationToken);
    }
}
