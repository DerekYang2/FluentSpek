namespace Spek.Net;

public enum AudioError
{
    None,
    CannotOpenFile,
    NoStreams,
    NoAudio,
    NoDuration,
    NoChannels,
    FfmpegNotFound,
    DecodingFailed,
}

// One audio stream as ffmpeg describes it. BitRate is in kb/s and is 0 when the bit depth is known, as in Spek.
public sealed record AudioStreamInfo(
    string CodecName,
    string CodecDescription,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int BitRate);

public sealed record AudioFileInfo(
    string Path,
    AudioError Error,
    double Duration,
    IReadOnlyList<AudioStreamInfo> Streams,
    string? ErrorDetail = null)
{
    public static AudioFileInfo Failed(string path, AudioError error, string? detail = null) => new(path, error, 0, [], detail);

    public AudioStreamInfo? GetStream(int stream) => stream >= 0 && stream < Streams.Count ? Streams[stream] : null;

    // The error for one stream: the file's own error first, then problems with the stream itself.
    public AudioError ErrorFor(int stream)
    {
        if (Error != AudioError.None) return Error;
        var info = GetStream(stream);
        if (info is null) return AudioError.NoAudio;
        if (info.Channels <= 0) return AudioError.NoChannels;
        return AudioError.None;
    }

    public long FrameCount(int stream) => GetStream(stream) is { } info ? (long)Math.Round(Duration * info.SampleRate) : 0;
}
