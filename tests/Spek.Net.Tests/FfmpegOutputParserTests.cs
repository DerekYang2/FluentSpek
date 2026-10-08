namespace Spek.Net.Tests;

[TestClass]
public sealed class FfmpegOutputParserTests
{
    private static readonly Dictionary<string, string> Decoders = FfmpegOutputParser.ParseDecoders("""
        Decoders:
         V..... = Video
         A..... = Audio
         ------
         A....D aac                  AAC (Advanced Audio Coding)
         A....D ac3                  ATSC A/52A (AC-3)
         AF...D flac                 FLAC (Free Lossless Audio Codec)
         A....D mp3float             MP3 (MPEG audio layer 3) (codec mp3)
         A....D mp3                  MP3 (MPEG audio layer 3)
         A....D opus                 Opus
         A....D pcm_s24le            PCM signed 24-bit little-endian
         V....D h264                 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10
        """);

    [TestMethod]
    public void ParsesDecoderList()
    {
        Assert.AreEqual("FLAC (Free Lossless Audio Codec)", Decoders["flac"]);
        Assert.AreEqual("MP3 (MPEG audio layer 3)", Decoders["mp3"]);
        Assert.IsFalse(Decoders.ContainsKey("h264"));
    }

    [TestMethod]
    public void Flac16()
    {
        var info = Parse("""
            Input #0, flac, from 'a16.flac':
              Duration: 00:00:02.00, start: 0.000000, bitrate: 253 kb/s
              Stream #0:0: Audio: flac, 44100 Hz, stereo, s16
            At least one output file must be specified
            """);

        Assert.AreEqual(AudioError.None, info.Error);
        Assert.AreEqual(2.0, info.Duration, 1e-9);
        AssertStream(info.Streams.Single(), "flac", "FLAC (Free Lossless Audio Codec)", 44100, 2, 16, 0);
    }

    [TestMethod]
    public void Mp3()
    {
        var info = Parse("""
            Input #0, mp3, from 'a.mp3':
              Duration: 00:00:02.04, start: 0.025057, bitrate: 324 kb/s
              Stream #0:0: Audio: mp3 (mp3float), 44100 Hz, stereo, fltp, 320 kb/s
            """);

        AssertStream(info.Streams.Single(), "mp3", "MP3 (MPEG audio layer 3)", 44100, 2, 0, 320);
        Assert.AreEqual(2.04, info.Duration, 1e-9);
    }

    [TestMethod]
    public void Mp3FromFfmpeg9WithStartAndCoverArt()
    {
        var info = Parse("""
            Input #0, mp3, from 'ladan.mp3':
              Duration: 00:03:53.32, start: 0.023021, bitrate: 239 kb/s
              Stream #0:0: Audio: mp3 (mp3float), 48000 Hz, stereo, fltp, 181 kb/s, start 0.023021
              Stream #0:1: Video: png, rgb24(pc, gbr/unknown/unknown), 1408x768, 90k tbr, 90k tbn, start 0.023022 (attached pic)
            """);

        AssertStream(info.Streams.Single(), "mp3", "MP3 (MPEG audio layer 3)", 48000, 2, 0, 181);
        Assert.AreEqual(233.32, info.Duration, 1e-9);
    }

    [TestMethod]
    public void AacInM4a()
    {
        var info = Parse("""
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'a.m4a':
              Duration: 00:00:02.00, start: 0.000000, bitrate: 239 kb/s
              Stream #0:0[0x1](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 232 kb/s (default)
              Stream #0:1[0x0]: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 500x500, 90k tbr, 90k tbn (attached pic)
            """);

        Assert.AreEqual(1, info.Streams.Count);
        AssertStream(info.Streams[0], "aac", "AAC (Advanced Audio Coding)", 44100, 2, 0, 232);
    }

    [TestMethod]
    public void OpusWithoutBitrate()
    {
        var info = Parse("""
            Input #0, ogg, from 'a.opus':
              Duration: 00:00:02.01, start: 0.000000, bitrate: 251 kb/s
              Stream #0:0: Audio: opus, 48000 Hz, stereo, fltp
            """);

        AssertStream(info.Streams.Single(), "opus", "Opus", 48000, 2, 0, 0);
    }

    [TestMethod]
    public void Wav24()
    {
        var info = Parse("""
            Input #0, wav, from 'a24.wav':
              Duration: 00:00:02.00, bitrate: 2117 kb/s
              Stream #0:0: Audio: pcm_s24le ([1][0][0][0] / 0x0001), 44100 Hz, stereo, s32 (24 bit), 2116 kb/s
            """);

        AssertStream(info.Streams.Single(), "pcm_s24le", "PCM signed 24-bit little-endian", 44100, 2, 24, 0);
    }

    [TestMethod]
    public void Ac3Surround()
    {
        var info = Parse("""
            [ac3 @ 000001a619c33540] Estimating duration from bitrate, this may be inaccurate
            Input #0, ac3, from 'a51.ac3':
              Duration: 00:00:02.02, start: 0.000000, bitrate: 448 kb/s
              Stream #0:0: Audio: ac3, 48000 Hz, 5.1(side), fltp, 448 kb/s
            """);

        AssertStream(info.Streams.Single(), "ac3", "ATSC A/52A (AC-3)", 48000, 6, 0, 448);
    }

    [TestMethod]
    public void TwoAudioStreams()
    {
        var info = Parse("""
            Input #0, matroska,webm, from 'two.mka':
              Duration: 00:00:02.00, start: 0.000000, bitrate: 977 kb/s
              Stream #0:0: Audio: flac, 44100 Hz, stereo, s32 (24 bit) (default)
                  Metadata:
                    DURATION        : 00:00:02.000000000
              Stream #0:1: Audio: flac, 44100 Hz, mono, s16
                    DURATION        : 00:00:02.000000000
            """);

        Assert.AreEqual(2, info.Streams.Count);
        AssertStream(info.Streams[0], "flac", "FLAC (Free Lossless Audio Codec)", 44100, 2, 24, 0);
        AssertStream(info.Streams[1], "flac", "FLAC (Free Lossless Audio Codec)", 44100, 1, 16, 0);
    }

    [TestMethod]
    public void MissingFile()
    {
        var info = Parse("""
            [in#0 @ 0000022518ea0240] Error opening input: No such file or directory
            Error opening input file C:\nope.flac.
            Error opening input files: No such file or directory
            """);

        Assert.AreEqual(AudioError.CannotOpenFile, info.Error);
        Assert.AreEqual("Error opening input files: No such file or directory", info.ErrorDetail);
    }

    [TestMethod]
    public void UnknownDuration()
    {
        var info = Parse("""
            [flac @ 00000235208d3380] Could not find codec parameters for stream 0 (Audio: flac, 0 channels): unspecified sample format
            Input #0, flac, from 'notaudio.flac':
              Duration: N/A, bitrate: N/A
              Stream #0:0: Audio: flac, 0 channels
            At least one output file must be specified
            """);

        Assert.AreEqual(AudioError.NoDuration, info.Error);
        Assert.AreEqual(0, info.Streams.Single().Channels);
    }

    [TestMethod]
    public void VideoOnly()
    {
        var info = Parse("""
            Input #0, matroska,webm, from 'v.mkv':
              Duration: 00:00:05.00, start: 0.000000, bitrate: 50 kb/s
              Stream #0:0: Video: h264 (High), yuv420p(progressive), 320x240, 25 fps, 25 tbr, 1k tbn
            """);

        Assert.AreEqual(AudioError.NoAudio, info.Error);
    }

    [TestMethod]
    [DataRow("mono", 1)]
    [DataRow("stereo", 2)]
    [DataRow("2.1", 3)]
    [DataRow("quad(side)", 4)]
    [DataRow("5.1(side)", 6)]
    [DataRow("7.1", 8)]
    [DataRow("7.1.4", 12)]
    [DataRow("hexadecagonal", 16)]
    [DataRow("3 channels", 3)]
    [DataRow("6 channels (FL+FR+FC+LFE+BL+BR)", 6)]
    [DataRow("FL+FR+LFE", 3)]
    public void ChannelLayouts(string layout, int channels)
    {
        Assert.AreEqual(channels, FfmpegOutputParser.ChannelCount(layout));
    }

    private static AudioFileInfo Parse(string stderr) =>
        FfmpegOutputParser.ParseInput("test", stderr, name => Decoders.GetValueOrDefault(name));

    private static void AssertStream(AudioStreamInfo stream, string codec, string description, int rate, int channels, int bits, int bitRate)
    {
        Assert.AreEqual(codec, stream.CodecName);
        Assert.AreEqual(description, stream.CodecDescription);
        Assert.AreEqual(rate, stream.SampleRate);
        Assert.AreEqual(channels, stream.Channels);
        Assert.AreEqual(bits, stream.BitsPerSample);
        Assert.AreEqual(bitRate, stream.BitRate);
    }
}
