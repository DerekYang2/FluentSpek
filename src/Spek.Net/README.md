# Spek.NET

Spek.NET turns an audio file into a spectrogram based on [Spek](https://github.com/alexkay/spek), with its palettes, window functions, dB scale and rulers. Analysis uses centred, overlapping FFTs on a file-anchored time grid and averages linear power into display columns. It has no UI. For a WinUI 3 control, use [Spek.NET.WinUI](https://www.nuget.org/packages/Spek.NET.WinUI).

Spek.NET decodes with `ffmpeg.exe`, which it runs as a separate process. The package doesn't include ffmpeg.

```csharp
using Spek.Net;

var ffmpeg = new Ffmpeg(Ffmpeg.Find([]) ?? throw new FileNotFoundException("ffmpeg isn't on PATH."));
var info = await ffmpeg.ProbeAsync(@"C:\Music\song.flac");
if (info.Error != AudioError.None) throw new InvalidOperationException(SpekDescription.ErrorText(info.Error));

var settings = new AnalysisSettings(Channel: 0, FftBits: 11, WindowFunction: WindowFunction.Hann);
var spectrogram = new Spectrogram(800, SpekDefaults.BinsFor(settings.FftBits), Palette.Spectrum, lowerDb: -120, upperDb: 0);
await SpekAnalyzer.AnalyzeAsync(ffmpeg, info, settings, spectrogram);

// spectrogram.Pixels holds 800 x 1025 BGRA pixels, with the highest frequency in the top row.
// spectrogram.GetValue(x, bin) is the level of one bin in dB.
```

- `Ffmpeg.ProbeAsync` reads the streams, sample rate, channels, bit depth, bitrate and duration from ffmpeg's output.
- `SpekAnalyzer.AnalyzeAsync` decodes one channel of one stream and fills the `Spectrogram` column by column, so a UI can draw it while it runs. Cancelling the token stops ffmpeg.
- `Spectrogram.SetRangeAndPalette` recolours without analysing again.
- `SpectrogramPipeline` separates fixed-grid FFT measurements from display columns, for samples from another decoder. Its existing constructor uses a hop of FFT size / 16. The extended constructor accepts the absolute `startFrame` and `hopSize`; `SpekDefaults.HopSizeFor` selects the same quality-first spacing used by the analyser. Feed PCM from `InputStartFrame` through `InputEndFrame`, clipped at the real file boundaries, then call `Complete`. Those input bounds include context on both sides of the visible interval. `HopSize` and `MeasurementsDone` expose the actual cadence and work count. The cancellation-aware `Push` and `Complete` overloads can stop between FFTs.
- `SpekDescription` and `Ruler` give Spek's description line and ruler ticks.
- `Spectrogram.GetProfile(firstColumn, endColumn, sampleRate)` measures a selected passage's spectrum. `SpectrumProfile.FindDrop()` returns an optional measured spectral drop, not an estimate of the original codec or bitrate. A deliberately low-pass-filtered master can produce the same result.
- `FrequencyRange` and `TimeRange` handle coordinates and anchored zoom. `EncoderReferences.Mp3Lame` lists nominal LAME stereo CBR low-pass targets as optional inspection guides, not universal MP3 boundaries.
- `SpekAnalyzer.AnalyzeRangeAsync` fills a separate spectrogram for an absolute time interval. It uses the same FFT centres as whole-file analysis and reads context before and after the requested interval. Only the requested interval contributes to its pixels. `Ffmpeg.DecodeRangeAsync` exposes the bounded PCM decode. Cancellation stops the child process.

The analyser uses approximately 1 ms or smaller hops, with at least 16 windows per FFT length. Changing pixel width does not change that grid. Each pixel is the time-weighted mean of overlapping measurement cells in linear power, converted to dB once. Silence contributes zero power instead of forcing an entire average to negative infinity. This is still Spek's normalized FFT power scale, not a calibrated power spectral density. The dense grid does not remove the chosen window's time/frequency tradeoff. See [the design and tradeoffs](https://github.com/DerekYang2/FluentSpek/blob/main/docs/fixed-grid-analysis.md).

Licensed under GPL-3.0, like Spek. Source: https://github.com/DerekYang2/FluentSpek
