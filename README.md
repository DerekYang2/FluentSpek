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
dotnet build
dotnet test
```

FluentSpek looks for ffmpeg in this order:

1. The path in its settings.
2. `ffmpeg\ffmpeg.exe` or `ffmpeg.exe` next to `FluentSpek.exe`.
3. `ffmpeg.exe` on `PATH`.

The tests that run ffmpeg use `FLUENTSPEK_FFMPEG`, then `PATH`, and report inconclusive if neither has it.

## Differences from Spek

- Every decoded frame belongs to exactly one column. Spek's worker counts some frames twice where its reader hands over a new block, so its columns run ahead of the audio. In spek-X 0.9.4, a tone that starts halfway through a 2-minute or 10-minute file appears 4 columns right of the middle of an 832-column plot.
- Values at or above the upper dB limit are red in the Spectrum palette. Spek's palette function skips the top value, so those pixels come out black. That shows when the limit is lowered with `U`, because with Hann windowing nothing reaches the default 0 dB limit.
- The Blackman-Harris window uses the exact cosine terms. Spek reads them from a table indexed modulo the window size.
- Changing the palette or the dB range recolours the existing analysis instead of decoding the file again.

## Credits

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## License

GPL-3.0, the same as Spek. See [LICENSE](LICENSE).
