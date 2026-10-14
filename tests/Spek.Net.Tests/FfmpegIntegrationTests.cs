using System.Diagnostics;

namespace Spek.Net.Tests;

// These run the real ffmpeg. They find it through FLUENTSPEK_FFMPEG or PATH, and are inconclusive without it.
[TestClass]
public sealed class FfmpegIntegrationTests
{
    private static string? ffmpegPath;
    private static string folder = "";

    [ClassInitialize]
    public static void Setup(TestContext _)
    {
        ffmpegPath = Ffmpeg.Find([Environment.GetEnvironmentVariable("FLUENTSPEK_FFMPEG")]);
        folder = Path.Combine(Path.GetTempPath(), "Spek.Net.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task ProbesAndAnalysesEachChannel()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("tones.flac",
            "-f", "lavfi", "-i", "aevalsrc=0.5*sin(2*PI*1000*t)|0.5*sin(2*PI*5000*t):s=44100:d=3",
            "-c:a", "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24");

        var info = await ffmpeg.ProbeAsync(file);

        Assert.AreEqual(AudioError.None, info.Error);
        Assert.AreEqual(3.0, info.Duration, 0.011);
        var stream = info.Streams.Single();
        Assert.AreEqual("flac", stream.CodecName);
        Assert.AreEqual(44100, stream.SampleRate);
        Assert.AreEqual(2, stream.Channels);
        Assert.AreEqual(24, stream.BitsPerSample);
        StringAssert.StartsWith(stream.CodecDescription, "FLAC");

        // 1 kHz and 5 kHz fall in bins 46 and 232 of a 2048-point FFT at 44.1 kHz.
        Assert.AreEqual(46, await PeakBin(ffmpeg, info, channel: 0));
        Assert.AreEqual(232, await PeakBin(ffmpeg, info, channel: 1));
    }

    [TestMethod]
    public async Task FindsEveryAudioStream()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("two.mka",
            "-f", "lavfi", "-i", "sine=f=440:d=2",
            "-f", "lavfi", "-i", "sine=f=3000:d=2",
            "-map", "0", "-map", "1", "-c:a", "flac");

        var info = await ffmpeg.ProbeAsync(file);

        Assert.AreEqual(2, info.Streams.Count);
        Assert.AreEqual(139, await PeakBin(ffmpeg, info, channel: 0, stream: 1));
    }

    [TestMethod]
    public async Task ReportsFilesItCannotOpen()
    {
        var ffmpeg = RequireFfmpeg();

        var missing = await ffmpeg.ProbeAsync(Path.Combine(folder, "missing.flac"));
        Assert.AreEqual(AudioError.CannotOpenFile, missing.Error);

        var text = Path.Combine(folder, "text.mp3");
        await File.WriteAllTextAsync(text, "not audio");
        Assert.AreNotEqual(AudioError.None, (await ffmpeg.ProbeAsync(text)).Error);
    }

    [TestMethod]
    public async Task CancellingStopsTheDecode()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("long.flac", "-f", "lavfi", "-i", "anoisesrc=d=600:r=44100", "-c:a", "flac", "-compression_level", "0");
        var info = await ffmpeg.ProbeAsync(file);
        var spectrogram = new Spectrogram(500, SpekDefaults.BinsFor(SpekDefaults.FftBits), Palette.Spectrum, -120, 0);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), spectrogram, cancellation.Token));

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(5), $"cancelling took {watch.Elapsed}");
        Assert.IsTrue(spectrogram.ColumnsDone < 500);
    }

    private static async Task<int> PeakBin(Ffmpeg ffmpeg, AudioFileInfo info, int channel, int stream = 0)
    {
        var settings = new AnalysisSettings(Stream: stream, Channel: channel);
        var spectrogram = new Spectrogram(50, SpekDefaults.BinsFor(settings.FftBits), Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, spectrogram);

        Assert.AreEqual(50, spectrogram.ColumnsDone);
        int x = 25, best = 0;
        for (int bin = 1; bin < spectrogram.Bins; bin++)
        {
            if (spectrogram.GetValue(x, bin) > spectrogram.GetValue(x, best)) best = bin;
        }
        return best;
    }

    [TestMethod]
    public async Task MeasuresTheSpectralDropAfterMp3IsReencodedAsFlac()
    {
        var ffmpeg = RequireFfmpeg();
        var mp3 = Generate("cutoff.mp3", "-f", "lavfi", "-i", "anoisesrc=d=8:r=48000:a=0.1:seed=42",
            "-ac", "2", "-c:a", "libmp3lame", "-b:a", "128k");
        var file = Generate("cutoff.flac", "-i", mp3, "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        var data = new Spectrogram(100, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), data);
        var drop = data.GetProfile(20, 80, 48000).FindDrop();
        Assert.IsNotNull(drop);
        Assert.AreEqual(17000, drop.FrequencyHz, 800);
        Assert.IsTrue(drop.DropDb >= 20);
        Assert.AreEqual("flac", info.Streams[0].CodecName);
    }

    [TestMethod]
    public async Task FullBandwidthNoiseDoesNotGetACutoffMarker()
    {
        var ffmpeg = RequireFfmpeg();
        var file = Generate("broadband.flac", "-f", "lavfi", "-i", "anoisesrc=d=8:r=48000:a=0.1:seed=42", "-c:a", "flac");
        var info = await ffmpeg.ProbeAsync(file);
        var data = new Spectrogram(100, 1025, Palette.Spectrum, -120, 0);
        await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, new AnalysisSettings(), data);
        Assert.IsNull(data.GetProfile(0, 100, 48000).FindDrop());
    }

    private static Ffmpeg RequireFfmpeg()
    {
        if (ffmpegPath is null) Assert.Inconclusive("ffmpeg not found. Set FLUENTSPEK_FFMPEG or add ffmpeg to PATH.");
        return new Ffmpeg(ffmpegPath);
    }

    private static string Generate(string name, params string[] args)
    {
        var output = Path.Combine(folder, name);
        var info = new ProcessStartInfo(ffmpegPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" }.Concat(args).Append(output))
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, stderr);
        return output;
    }
}
