# Spek.NET.WinUI

`SpekView` is a WinUI 3 control that shows [Spek](https://github.com/alexkay/spek)'s spectrogram of an audio file and draws the columns as the file decodes. It's drawn with Win2D and uses [Spek.NET](https://www.nuget.org/packages/Spek.NET) for the analysis.

```xml
<Page xmlns:spek="using:Spek.Net.WinUI">
    <spek:SpekView x:Name="Spectrogram" FilePath="C:\Music\song.flac" />
</Page>
```

```csharp
Spectrogram.FfmpegPath = @"C:\Tools\ffmpeg.exe"; // optional; otherwise ffmpeg is taken from PATH
Spectrogram.Palette = Palette.Sox;
await Spectrogram.SaveAsync(@"C:\Temp\song.png");
```

- Changing `FilePath`, `Stream`, `Channel`, `FftBits` or `WindowFunction`, or the control's width, analyses again. Changing `Palette`, `LowerDb` or `UpperDb` recolours what's there.
- `StreamCount`, `ChannelCount`, `Description` and `IsAnalyzing` report the current file, and `AnalysisCompleted` fires when a file is done.
- `ShowFullPath`, `ShowDetailedDescription`, `BrandName`, `BrandVersion` and `Interpolation` change how it looks.
- With focus, the control takes Spek's keys: `c`, `f`, `l`, `p`, `s`, `u` and `w`, with Shift to step back.
- `SpekExporter.ExportAsync` renders a file to a PNG without a window.

The control is written in C# with no XAML file, so there's no compiled XAML to merge into your app. It needs Windows App SDK 2.5.1 or later, Win2D 1.4.0, and an app built for x64 or ARM64.

Licensed under GPL-3.0, like Spek. Source: https://github.com/DerekYang2/FluentSpek
