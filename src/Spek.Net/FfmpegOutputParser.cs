using System.Globalization;
using System.Text.RegularExpressions;

namespace Spek.Net;

// Reads the input description ffmpeg prints to stderr for "ffmpeg -i <file>", for example
//   Duration: 00:05:00.00, start: 0.000000, bitrate: 1416 kb/s
//   Stream #0:0: Audio: flac, 44100 Hz, stereo, s32 (24 bit)
internal static partial class FfmpegOutputParser
{
    // FFmpeg's named channel layouts. Numeric names such as "5.1" or "7.1.4" are summed instead.
    private static readonly Dictionary<string, int> NamedLayouts = new(StringComparer.Ordinal)
    {
        ["mono"] = 1,
        ["stereo"] = 2,
        ["binaural"] = 2,
        ["downmix"] = 2,
        ["quad"] = 4,
        ["hexagonal"] = 6,
        ["octagonal"] = 8,
        ["cube"] = 8,
        ["hexadecagonal"] = 16,
    };

    // Spek ignores the bit depth these decoders report.
    private static readonly HashSet<string> NoBitDepthCodecs = new(StringComparer.Ordinal) { "aac", "musepack8", "wmav1", "wmav2" };

    public static AudioFileInfo ParseInput(string path, string stderr, Func<string, string?>? describeCodec = null)
    {
        var lines = stderr.Replace("\r", "").Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith("Input #0", StringComparison.Ordinal));
        if (start < 0)
        {
            var detail = lines.LastOrDefault(line => line.Trim().Length > 0)?.Trim();
            return AudioFileInfo.Failed(path, AudioError.CannotOpenFile, detail);
        }

        double? duration = null;
        bool durationUnknown = false;
        int streamCount = 0;
        var streams = new List<AudioStreamInfo>();

        for (int i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("Output #", StringComparison.Ordinal) || line.StartsWith("Stream mapping", StringComparison.Ordinal)
                || line.StartsWith("Input #", StringComparison.Ordinal))
            {
                break;
            }

            var durationMatch = DurationRegex().Match(line);
            if (durationMatch.Success && duration is null && !durationUnknown)
            {
                if (durationMatch.Groups[1].Value == "N/A") durationUnknown = true;
                else duration = ParseDuration(durationMatch.Groups[1].Value);
                continue;
            }

            var streamMatch = StreamRegex().Match(line);
            if (!streamMatch.Success) continue;
            streamCount++;
            if (streamMatch.Groups[1].Value == "Audio")
            {
                streams.Add(ParseAudioStream(streamMatch.Groups[2].Value, describeCodec));
            }
        }

        if (streamCount == 0) return AudioFileInfo.Failed(path, AudioError.NoStreams);
        if (streams.Count == 0) return AudioFileInfo.Failed(path, AudioError.NoAudio);
        if (duration is null or <= 0) return new AudioFileInfo(path, AudioError.NoDuration, 0, streams);
        return new AudioFileInfo(path, AudioError.None, duration.Value, streams);
    }

    // Parses the part after "Audio: ", such as "aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 192 kb/s (default)".
    internal static AudioStreamInfo ParseAudioStream(string description, Func<string, string?>? describeCodec = null)
    {
        var parts = SplitTopLevel(description);
        var codecPart = parts.Count > 0 ? parts[0] : "";
        int nameEnd = codecPart.IndexOfAny([' ', '(']);
        string codec = nameEnd < 0 ? codecPart : codecPart[..nameEnd];

        int sampleRate = 0, channels = 0, bits = 0, bitRate = 0;
        string? sampleFormat = null;
        foreach (var part in parts.Skip(1))
        {
            Match m;
            if ((m = SampleRateRegex().Match(part)).Success)
            {
                sampleRate = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if ((m = BitRateRegex().Match(part)).Success)
            {
                bitRate = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if ((m = SampleFormatRegex().Match(part)).Success)
            {
                sampleFormat = m.Groups[1].Value;
                if (m.Groups[2].Success) bits = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else if (channels == 0 && sampleRate > 0)
            {
                channels = ChannelCount(part);
            }
        }

        // ffmpeg prints "(24 bit)" only when the depth differs from the sample format, so 16-bit FLAC shows just "s16".
        // Lossy decoders that output floats report no depth, which leaves 0.
        if (bits == 0 && sampleFormat is not null)
        {
            bool isFloat = sampleFormat is "flt" or "dbl";
            if (!isFloat || codec.StartsWith("pcm_f", StringComparison.Ordinal)) bits = FormatBits(sampleFormat);
        }
        if (NoBitDepthCodecs.Contains(codec)) bits = 0;
        if (bits > 0) bitRate = 0;

        string descriptionText = describeCodec?.Invoke(codec) ?? codec;
        return new AudioStreamInfo(codec, descriptionText, sampleRate, channels, bits, bitRate);
    }

    internal static int ChannelCount(string layout)
    {
        var m = ChannelsRegex().Match(layout);
        if (m.Success) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

        int paren = layout.IndexOf('(');
        string name = paren >= 0 ? layout[..paren] : layout;
        if (NamedLayouts.TryGetValue(name, out int named)) return named;

        if (NumericLayoutRegex().IsMatch(name))
        {
            return name.Split('.').Sum(n => int.Parse(n, CultureInfo.InvariantCulture));
        }

        if (layout.Contains('+')) return layout.Split('+').Length;
        return 0;
    }

    // Parses the decoder list from "ffmpeg -decoders" into short name and description.
    internal static Dictionary<string, string> ParseDecoders(string stdout)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in stdout.Replace("\r", "").Split('\n'))
        {
            var m = DecoderRegex().Match(raw);
            if (m.Success) result.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim());
        }
        return result;
    }

    private static int FormatBits(string format) => format switch
    {
        "u8" => 8,
        "s16" => 16,
        "s32" or "flt" => 32,
        "s64" or "dbl" => 64,
        _ => 0,
    };

    private static double ParseDuration(string text)
    {
        var p = text.Split(':');
        return int.Parse(p[0], CultureInfo.InvariantCulture) * 3600
            + int.Parse(p[1], CultureInfo.InvariantCulture) * 60
            + double.Parse(p[2], CultureInfo.InvariantCulture);
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']' && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    [GeneratedRegex(@"^\s*Duration: (N/A|\d+:\d{2}:\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"^\s*Stream #0:\d+\S*: (\w+): (.*)$")]
    private static partial Regex StreamRegex();

    [GeneratedRegex(@"^(\d+) Hz$")]
    private static partial Regex SampleRateRegex();

    [GeneratedRegex(@"^(\d+) kb/s")]
    private static partial Regex BitRateRegex();

    [GeneratedRegex(@"^(u8|s16|s32|s64|flt|dbl)p?(?: \((\d+) bit\))?(?: |$)")]
    private static partial Regex SampleFormatRegex();

    [GeneratedRegex(@"^(\d+) channels?\b")]
    private static partial Regex ChannelsRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)*$")]
    private static partial Regex NumericLayoutRegex();

    [GeneratedRegex(@"^\s*A\S{5}\s+(\S+)\s+(.+)$")]
    private static partial Regex DecoderRegex();
}
