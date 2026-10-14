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
.\scripts\Get-Ffmpeg.ps1            # ffmpeg for x64 into .\ffmpeg; add -Architecture x64, arm64 for both
dotnet build src\FluentSpek          # add -p:Platform=arm64 for ARM64
dotnet test
```

`Get-Ffmpeg.ps1` downloads an audio-only LGPL build of FFmpeg from [ffmpeg-audio](https://github.com/DerekYang2/ffmpeg-audio), about 8 MB instead of 130 MB for a full build, into `ffmpeg\win-x64` or `ffmpeg\win-arm64`, which git ignores. It checks each zip's SHA-256 against the hashes in the script. The app build copies `ffmpeg.exe`, its licenses and `BUILD.txt` to `ffmpeg\` next to `FluentSpek.exe`. To move to a newer ffmpeg-audio release, change the release tag and the two hashes in the script.

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

Settings (Ctrl+E) has the dB limits, the full path and detailed description options, and the ffmpeg path. Columns appear as soon as they're analysed, as in Spek and spek-X. There is no artificial reveal delay.

Settings are saved in `%LOCALAPPDATA%\FluentSpek\settings.json`.

### Command line

```
FluentSpek [FILE] [PNG] [WIDTH] [HEIGHT]
```

With a PNG path, FluentSpek saves the spectrogram at WIDTH × HEIGHT pixels (1000 × 600 by default) and exits without opening a window. It returns 0 on success and 1 if the file or ffmpeg can't be read. In spek-X, WIDTH and HEIGHT set the window size, so the PNG comes out smaller than the numbers you pass.

## Releasing

`.github/workflows/release.yml` publishes a version when its tag is pushed:

1. Set `<Version>` in `Directory.Build.props`, commit, and push.
2. Tag the commit with the same version and push the tag: `git tag v0.1.0` and `git push origin v0.1.0`.

The workflow runs the tests, checks that the tag matches the version, and pushes Spek.NET and Spek.NET.WinUI to nuget.org with their symbol packages. It also builds `FluentSpek-v0.1.0-win-x64.zip` and `FluentSpek-v0.1.0-win-arm64.zip`, each with the matching ffmpeg, and attaches them to a GitHub release. A tag with a hyphen, such as `v0.2.0-beta.1`, makes a prerelease. Running the workflow by hand from the Actions tab builds everything without publishing.

nuget.org uses Trusted Publishing, so the repository stores no API key. It needs a policy on nuget.org (your name > Trusted Publishing, with owner `DerekYang2`, repository `FluentSpek` and workflow `release.yml`) and a repository secret `NUGET_USER` that holds the nuget.org profile name.

## Differences from Spek

- Every decoded frame belongs to exactly one column. Spek's worker counts some frames twice where its reader hands over a new block, so its columns run ahead of the audio. In spek-X 0.9.4, a tone that starts halfway through a 2-minute or 10-minute file appears 4 columns right of the middle of an 832-column plot.
- Values at or above the upper dB limit are red in the Spectrum palette. Spek's palette function skips the top value, so those pixels come out black. That shows when the limit is lowered with `U`, because with Hann windowing nothing reaches the default 0 dB limit.
- The Blackman-Harris window uses the exact cosine terms. Spek reads them from a table indexed modulo the window size.
- Changing the palette or the dB range recolours the existing analysis instead of decoding the file again.

## Credits

Based on [Spek](https://github.com/alexkay/spek) and [spek-X](https://github.com/MikeWang000000/spek-X).

## License

GPL-3.0, the same as Spek. See [LICENSE](LICENSE).
