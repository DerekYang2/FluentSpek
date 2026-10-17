namespace Spek.Net;

public sealed record AnalysisSettings(
    int Stream = 0,
    int Channel = 0,
    int FftBits = SpekDefaults.FftBits,
    WindowFunction WindowFunction = WindowFunction.Hann);

public static class SpekAnalyzer
{
    // Analyse this interval on the same global FFT grid as the overview, including context on both sides.
    public static Task AnalyzeRangeAsync(
        Ffmpeg ffmpeg, AudioFileInfo info, AnalysisSettings settings, Spectrogram target, TimeRange range,
        CancellationToken cancellationToken = default)
    {
        if (info.ErrorFor(settings.Stream) is var error and not AudioError.None)
            throw new ArgumentException($"The file can't be analysed: {SpekDescription.ErrorText(error)}.", nameof(info));
        int rate = info.GetStream(settings.Stream)!.SampleRate;
        if (rate <= 0 || range.Span <= 0 || range.End > info.Duration)
            throw new ArgumentOutOfRangeException(nameof(range), "The time range must fit the audio stream.");
        if (settings.Channel < 0 || settings.Channel >= info.GetStream(settings.Stream)!.Channels)
            throw new ArgumentOutOfRangeException(nameof(settings));
        if (target.Bins != SpekDefaults.BinsFor(settings.FftBits))
            throw new ArgumentException("The spectrogram's bin count doesn't match the FFT size.", nameof(target));

        long first = (long)Math.Round(range.Start * rate);
        long end = (long)Math.Round(range.End * rate);
        if (end <= first) throw new ArgumentOutOfRangeException(nameof(range), "The interval must contain at least one audio sample.");
        return Task.Run(async () =>
        {
            var pipeline = new SpectrogramPipeline(settings.FftBits, settings.WindowFunction, target.Columns, end - first,
                target.SetColumn, first, SpekDefaults.HopSizeFor(settings.FftBits, rate));
            long decodeEnd = Math.Min(info.FrameCount(settings.Stream), pipeline.InputEndFrame);
            await ffmpeg.DecodeRangeAsync(info.Path, settings.Stream, settings.Channel,
                new TimeRange(pipeline.InputStartFrame / (double)rate, decodeEnd / (double)rate),
                samples => pipeline.Push(samples, cancellationToken), cancellationToken).ConfigureAwait(false);
            pipeline.Complete(cancellationToken);
        }, cancellationToken);
    }

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
            int rate = info.GetStream(settings.Stream)!.SampleRate;
            var pipeline = new SpectrogramPipeline(
                settings.FftBits, settings.WindowFunction, target.Columns, info.FrameCount(settings.Stream), target.SetColumn,
                0, SpekDefaults.HopSizeFor(settings.FftBits, rate));
            long remaining = info.FrameCount(settings.Stream);
            await ffmpeg.DecodeAsync(info.Path, settings.Stream, settings.Channel,
                samples =>
                {
                    int count = (int)Math.Min(remaining, samples.Length);
                    if (count > 0) pipeline.Push(samples[..count], cancellationToken);
                    remaining -= count;
                }, cancellationToken)
                .ConfigureAwait(false);
            pipeline.Complete(cancellationToken);
        }, cancellationToken);
    }
}
