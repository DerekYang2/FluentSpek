# Fixed-grid analysis

This change separates audio measurements from the displayed image. It prioritizes dense analysis for zoom rather than the old pipeline's low CPU cost. It does not change the FFT size automatically or eliminate the time/frequency tradeoff.

## Measurement grid

For an FFT size N and sample rate R, the analyser uses a hop in samples of:

```text
H = min(N / 16, max(1, round(R / 1000)))
centres = 0, H, 2H, 3H, ...
window at centre C = [C - N/2, C + N/2)
```

The hop is about 1 ms or less, with at least 93.75% overlap. It depends on FFT size and sample rate, never on the number of image columns or the visible time range. At 48 kHz, 512 samples use a 32-sample hop, and 2048/8192/16384 samples use a 48-sample hop.

A long FFT still mixes audio across a long time window. More overlapping measurements sample that result densely; they do not turn an 8192-sample window into a short observation. Zoom beyond the measurement spacing reuses the nearest cells rather than inventing independent FFTs per pixel.

## Centred timestamps and context

Each FFT is centred on its timestamp rather than ending there. This removes the systematic half-window delay of a trailing analysis. The window still spreads transient energy over time; the change makes that spread symmetric rather than placing it after the event.

Range analysis starts at the first required window's left edge and decodes through the final required window's right edge. That includes real audio outside the visible passage. It uses zero padding only beyond the file's boundaries. Finishing an unexpectedly short decode pads the final FFT context, not the rest of an overestimated file duration.

The grid is anchored at the start of the file. Seeking into a passage does not restart it at the passage boundary. Compressed-decoder seek behavior and metadata timing precision can still limit exact cross-decode agreement; the strict equivalence tests use lossless audio.

## Drawing measurements into pixels

Each measurement owns the time cell nearest its centre. A pixel averages the linear power of cells that overlap its time interval, weighted by the overlap duration. Conversion to dB happens after that average.

For example, equal time at power 1 and power 0 produces power 0.5, about -3 dB. Averaging 0 dB and negative infinity directly would incorrectly erase the result. Quiet and silent measurements must contribute their proper energy, not dominate through logarithmic averaging.

Pixel boundaries use exact integer ratios with Int128 intermediate coordinates, so more pixels than audio samples do not produce empty-time pixels. Aligned groups of fine pixels aggregate to the corresponding coarse pixel's power, within floating-point precision.

This is a mean-energy display, not peak hold. Brief events can still become faint in a broad overview. Zoom separates them from the surrounding quiet time. Peak hold would preserve their maximum strength but could overemphasize a brief tone or noise burst; it is not added by this change.

## Scale and appearance

The existing normalized FFT power scale, window functions, palettes and dB limits remain. No audio gain is applied. This is not a calibrated power spectral density, and levels from different FFT sizes are not a universal loudness comparison.

Some content will appear brighter than in 0.2.0 because an arithmetic power average differs from its old dB average. This is a measurement change, not simply a cosmetic sharpening filter. The window still determines real time/frequency resolution. Image interpolation also remains a display choice and can soften drawn pixels.

The profile and cutoff continue to use the cached overview's selected passage. Zoom refinement does not change that selection or replace those measurements. The 350 ms refinement debounce and immediate cached overview reset are unchanged.

## Work, memory and cancellation

The pipeline streams. It keeps a ring buffer, FFT work arrays and one column's power sums, not a whole-file history of dense FFTs. The existing overview/detail pixel buffers and one-interval cache remain. Extra working memory scales with FFT size, not track duration.

The main cost is CPU. Whole-track analysis at large FFT sizes is much slower. Range refinement only analyses the visible interval plus context, so its work remains bounded by the requested passage. Cancellation is checked between FFTs as well as at decoder reads, and work remains off the UI thread.

### Synthetic benchmark

One warmed run on this development machine, 48 kHz mono noise, 1000 output columns, Release build. These timings include pipeline analysis and pixel colouring, not ffmpeg decoding or GPU rendering. They are observations, not performance guarantees.

| FFT | 0.2.0, 30 s overview | Fixed grid, 30 s overview | 0.2.0, 1 s crop | Fixed grid, 1 s crop |
| --- | ---: | ---: | ---: | ---: |
| 512 | 59 ms | 283 ms | 33 ms | 18 ms |
| 2048 | 94 ms | 852 ms | 49 ms | 53 ms |
| 8192 | 240 ms | 3553 ms | 217 ms | 217 ms |
| 16384 | 467 ms | 8051 ms | 477 ms | 482 ms |

Allocation stayed within about 0.07 MiB of the previous pipeline for each case. The 1000-column 16384-sample benchmark uses larger output buffers than the UI's 32 MiB refinement cap permits; the benchmark keeps the output dimensions identical between implementations.

## Verification

- Coarse pixels equal the energy average of aligned fine pixels.
- Crops share the overview's global grid, including odd hop sizes and file edges.
- Lossless ffmpeg crops match corresponding overview pixels at 44.1, 48 and 96 kHz within 0.0001 dB.
- Decoder chunk sizes do not change output, and FFT count does not depend on image width.
- An impulse stays near its actual timestamp instead of shifting by half an FFT.
- Silence, partial cells, early EOF, extra samples, fractional pixel widths and cancellation have dedicated tests.
- Existing channel, multi-stream, MP3/Opus range, cutoff and FFT reference tests remain in the suite.
