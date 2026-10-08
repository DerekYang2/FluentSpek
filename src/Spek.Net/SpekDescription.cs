namespace Spek.Net;

public static class SpekDescription
{
    // The line under the file name, such as "FLAC (Free Lossless Audio Codec), 24 bits, 44100 Hz, channel 1 / 2".
    public static string Describe(AudioFileInfo info, AnalysisSettings settings, bool detailed, AudioError extraError = AudioError.None)
    {
        var error = info.ErrorFor(settings.Stream);
        if (error == AudioError.None) error = extraError;

        var items = new List<string>();
        if (info.GetStream(settings.Stream) is { } stream)
        {
            if (stream.CodecDescription.Length > 0) items.Add(stream.CodecDescription);
            if (stream.BitRate > 0) items.Add($"{stream.BitRate} kbps");
            if (stream.SampleRate > 0) items.Add($"{stream.SampleRate} Hz");
            if (stream.BitsPerSample > 0 && stream.BitRate == 0)
                items.Add(stream.BitsPerSample == 1 ? "1 bit" : $"{stream.BitsPerSample} bits");
            if (stream.Channels > 0) items.Add($"channel {settings.Channel + 1} / {stream.Channels}");
            if (detailed && error == AudioError.None)
            {
                items.Add($"W:{1 << settings.FftBits}");
                items.Add($"F:{WindowFunctions.DisplayName(settings.WindowFunction)}");
            }
        }

        var description = string.Join(", ", items);
        var errorText = ErrorText(error);
        if (description.Length == 0) return errorText;
        if (settings.Stream < info.Streams.Count && info.Streams.Count != 1)
            return $"Stream {settings.Stream + 1} / {info.Streams.Count}: {description}";
        if (errorText.Length > 0) return $"{errorText}: {description}";
        return description;
    }

    public static string ErrorText(AudioError error) => error switch
    {
        AudioError.None => "",
        AudioError.CannotOpenFile => "Cannot open input file",
        AudioError.NoStreams => "Cannot find stream info",
        AudioError.NoAudio => "The file contains no audio streams",
        AudioError.NoDuration => "Unknown duration",
        AudioError.NoChannels => "No audio channels",
        AudioError.FfmpegNotFound => "Cannot find ffmpeg",
        AudioError.DecodingFailed => "Decoding failed",
        _ => error.ToString(),
    };
}
