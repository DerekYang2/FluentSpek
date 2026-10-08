namespace Spek.Net.Tests;

[TestClass]
public sealed class DescriptionAndRulerTests
{
    private static readonly AudioStreamInfo Flac = new("flac", "FLAC (Free Lossless Audio Codec)", 44100, 2, 24, 0);
    private static readonly AudioStreamInfo Mp3 = new("mp3", "MP3 (MPEG audio layer 3)", 44100, 2, 0, 320);

    [TestMethod]
    public void DescribesOneStream()
    {
        var info = new AudioFileInfo("a.flac", AudioError.None, 300, [Flac]);

        Assert.AreEqual(
            "FLAC (Free Lossless Audio Codec), 44100 Hz, 24 bits, channel 1 / 2",
            SpekDescription.Describe(info, new AnalysisSettings(), detailed: false));
        Assert.AreEqual(
            "FLAC (Free Lossless Audio Codec), 44100 Hz, 24 bits, channel 2 / 2, W:4096, F:Blackman-Harris",
            SpekDescription.Describe(info, new AnalysisSettings(Channel: 1, FftBits: 12, WindowFunction: WindowFunction.BlackmanHarris), detailed: true));
    }

    [TestMethod]
    public void DescribesBitrateInsteadOfBits()
    {
        var info = new AudioFileInfo("a.mp3", AudioError.None, 300, [Mp3]);
        Assert.AreEqual("MP3 (MPEG audio layer 3), 320 kbps, 44100 Hz, channel 1 / 2",
            SpekDescription.Describe(info, new AnalysisSettings(), detailed: false));
    }

    [TestMethod]
    public void NumbersStreamsWhenThereAreSeveral()
    {
        var info = new AudioFileInfo("two.mka", AudioError.None, 2, [Flac, Mp3]);
        Assert.AreEqual("Stream 2 / 2: MP3 (MPEG audio layer 3), 320 kbps, 44100 Hz, channel 1 / 2",
            SpekDescription.Describe(info, new AnalysisSettings(Stream: 1), detailed: false));
    }

    [TestMethod]
    public void ShowsErrors()
    {
        Assert.AreEqual("Cannot open input file",
            SpekDescription.Describe(AudioFileInfo.Failed("x", AudioError.CannotOpenFile), new AnalysisSettings(), false));
        Assert.AreEqual("Unknown duration: FLAC (Free Lossless Audio Codec), 44100 Hz, 24 bits, channel 1 / 2",
            SpekDescription.Describe(new AudioFileInfo("x", AudioError.NoDuration, 0, [Flac]), new AnalysisSettings(), true));
    }

    [TestMethod]
    public void TimeRulerPicksAStepThatFits()
    {
        // 300 s over 848 px, with "00:00" 30 px wide: the first step at least 45 px apart is 20 s.
        var ticks = Ruler.Ticks(0, 300, Ruler.TimeSteps, 1.5, 848 / 300.0, 0, 30, vertical: false);

        Assert.AreEqual(16, ticks.Count);
        CollectionAssert.AreEquivalent(
            new[] { 0, 300 }.Concat(Enumerable.Range(1, 14).Select(i => i * 20)).ToList(),
            ticks.Select(t => t.Value).ToList());
        Assert.AreEqual(848, ticks.Single(t => t.Value == 300).Position, 1e-9);
    }

    [TestMethod]
    public void FrequencyRulerCountsUpFromTheBottom()
    {
        var ticks = Ruler.Ticks(0, 22050, Ruler.FrequencySteps, 3.0, 500 / 22050.0, 0, 14, vertical: true);

        Assert.AreEqual(500, ticks.Single(t => t.Value == 0).Position, 1e-9);
        Assert.AreEqual(0, ticks.Single(t => t.Value == 22050).Position, 1e-9);
        Assert.IsTrue(ticks.Any(t => t.Value == 2000));
        Assert.IsFalse(ticks.Any(t => t.Value == 1000));
    }

    [TestMethod]
    public void Formats()
    {
        Assert.AreEqual("4:05", Ruler.FormatTime(245));
        Assert.AreEqual("16 kHz", Ruler.FormatFrequency(16000));
        Assert.AreEqual("-120 dB", Ruler.FormatDensity(120));
    }
}
