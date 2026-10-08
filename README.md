# FluentSpek

FluentSpek is a Fluent UI spectrogram analyser for Windows. It draws a spectrogram of an audio file as it decodes it.

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## Projects

| Project | What it is |
| --- | --- |
| `src/Spek.Net` | Spek.NET. Decodes audio with ffmpeg and turns it into spectrogram columns. Has no UI and targets `net10.0`. |
| `src/Spek.Net.WinUI` | Spek.NET.WinUI. The `SpekView` control, which draws the spectrogram with Win2D, and PNG export. |
| `src/FluentSpek` | The standalone app. |
| `tests/Spek.Net.Tests` | MSTest tests for Spek.NET. |

## Building

You need the .NET 10 SDK and ffmpeg.

```powershell
.\scripts\Get-Ffmpeg.ps1            # ffmpeg 9.0 for x64 into .\ffmpeg; add -Architecture x64, arm64 for both
dotnet build src\FluentSpek          # add -p:Platform=arm64 for ARM64
dotnet test
```

`Get-Ffmpeg.ps1` downloads BtbN's LGPL build of FFmpeg 9.0 into `ffmpeg\win-x64` or `ffmpeg\win-arm64`, which git ignores. The app build copies it to `ffmpeg\ffmpeg.exe` next to `FluentSpek.exe`.

FluentSpek looks for ffmpeg in this order:

1. The path in its settings.
2. `ffmpeg\ffmpeg.exe` or `ffmpeg.exe` next to `FluentSpek.exe`.
3. `ffmpeg.exe` on `PATH`.

The tests that run ffmpeg use `FLUENTSPEK_FFMPEG`, then `PATH`, and report inconclusive if neither has it.

## Using FluentSpek

Open a file with the Open button, Ctrl+O, by dropping it on the window, or from the command line. The toolbar changes the palette, window function, FFT size, channel and stream, and Ctrl+S saves the view as a PNG.

With the spectrogram focused, Spek's keys work too. Each key steps forward, and with Shift it steps back:

| Key | Changes |
| --- | --- |
| `c` | Channel |
| `f` | Window function: Hann, Hamming, Blackman-Harris |
| `l` | Lower limit of the dB range |
| `p` | Palette: Spectrum, SoX, Mono |
| `s` | Audio stream |
| `u` | Upper limit of the dB range |
| `w` | FFT size, from 256 to 16384 samples |

Settings (Ctrl+E) has the dB limits, the full path and detailed description options, the ffmpeg path, and a minimum fill-in time. Short files are analysed in a fraction of a second, so by default the spectrogram still fills in from left to right over at least 0.75 seconds. Set it to 0 to show each column as soon as it's ready.

Settings are saved in `%LOCALAPPDATA%\FluentSpek\settings.json`.

### Command line

```
FluentSpek [FILE] [PNG] [WIDTH] [HEIGHT]
```

With a PNG path, FluentSpek saves the spectrogram at WIDTH × HEIGHT pixels (1000 × 600 by default) and exits without opening a window. It returns 0 on success and 1 if the file or ffmpeg can't be read. In spek-X, WIDTH and HEIGHT set the window size, so the PNG comes out smaller than the numbers you pass.

## Differences from Spek

- Every decoded frame belongs to exactly one column. Spek's worker counts some frames twice where its reader hands over a new block, so its columns run ahead of the audio. In spek-X 0.9.4, a tone that starts halfway through a 2-minute or 10-minute file appears 4 columns right of the middle of an 832-column plot.
- Values at or above the upper dB limit are red in the Spectrum palette. Spek's palette function skips the top value, so those pixels come out black. That shows when the limit is lowered with `U`, because with Hann windowing nothing reaches the default 0 dB limit.
- The Blackman-Harris window uses the exact cosine terms. Spek reads them from a table indexed modulo the window size.
- Changing the palette or the dB range recolours the existing analysis instead of decoding the file again.

## Credits

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## License

GPL-3.0, the same as Spek. See [LICENSE](LICENSE).
