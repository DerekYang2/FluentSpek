# Spek.NET

Spek.NET turns an audio file into a spectrogram the way [Spek](https://github.com/alexkay/spek) does, with Spek's palettes, window functions, dB scale and rulers. It has no UI. For a WinUI 3 control that draws the result, use [Spek.NET.WinUI](https://www.nuget.org/packages/Spek.NET.WinUI).

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
- `SpectrogramPipeline` is the column maths on its own, for samples from another decoder.
- `SpekDescription` and `Ruler` give Spek's description line and ruler ticks.

Licensed under GPL-3.0, like Spek. Source: https://github.com/DerekYang2/FluentSpek
