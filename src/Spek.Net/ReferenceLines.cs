namespace Spek.Net;

public enum ReferenceLines
{
    None,
    Mp3Lame,
}

public readonly record struct FrequencyReference(int BitRateKbps, int FrequencyHz);

public static class EncoderReferences
{
    // LAME 3.100, libmp3lame/lame.c, optimum_bandwidth's nominal stereo CBR low-pass targets.
    // These are not MP3 requirements. The transition band varies with sample rate, and VBR, mono, custom filters
    // and other encoders can differ. A cutoff cannot prove a file's original bitrate or encoding history.
    // https://github.com/ffmpegwasm/lame/blob/master/libmp3lame/lame.c
    public static IReadOnlyList<FrequencyReference> Mp3Lame { get; } = Array.AsReadOnly<FrequencyReference>(
    [
        new(128, 17000),
        new(192, 18600),
        new(256, 19700),
        new(320, 20500),
    ]);
}
