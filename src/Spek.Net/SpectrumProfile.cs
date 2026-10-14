namespace Spek.Net;

// A measurement of a sharp drop in the profile, not an estimate of the original bitrate or a transcode verdict.
public sealed record SpectralDrop(double FrequencyHz, double DropDb);

public sealed class SpectrumProfile
{
    private readonly float[] levels;

    public SpectrumProfile(IEnumerable<float> levelsDb, double binWidthHz, int includedColumns)
    {
        if (!double.IsFinite(binWidthHz) || binWidthHz <= 0) throw new ArgumentOutOfRangeException(nameof(binWidthHz));
        if (includedColumns < 0) throw new ArgumentOutOfRangeException(nameof(includedColumns));
        levels = levelsDb.ToArray();
        if (levels.Length < 2 || levels.Any(v => float.IsNaN(v) || float.IsPositiveInfinity(v)))
            throw new ArgumentException("A profile needs at least two valid spectral levels.", nameof(levelsDb));
        BinWidthHz = binWidthHz;
        IncludedColumns = includedColumns;
    }

    public ReadOnlyMemory<float> LevelsDb => levels;
    public double BinWidthHz { get; }
    public int IncludedColumns { get; }
    public double Nyquist => (levels.Length - 1) * BinWidthHz;

    // Looks for a broad, sustained high-frequency drop, using the full profile regardless of the view's zoom.
    // Sparse tones, silence, a narrow notch and a gradual roll-off should not produce a marker. A sharp mastering
    // low-pass can still produce one: this measures the edge and cannot explain what caused it.
    public SpectralDrop? FindDrop()
    {
        if (IncludedColumns == 0 || Nyquist < 10000 || levels.Max() < -95) return null;

        // Prefix sums keep band averages linear in the number of FFT bins rather than scanning the whole tail
        // again at every candidate. Average linear power, then convert back to dB.
        var sums = new double[levels.Length + 1];
        for (int i = 0; i < levels.Length; i++) sums[i + 1] = sums[i] + Math.Pow(10, levels[i] / 10.0);

        double BandLevel(double low, double high)
        {
            int from = Math.Clamp((int)Math.Ceiling(low / BinWidthHz), 0, levels.Length - 1);
            int to = Math.Clamp((int)Math.Ceiling(high / BinWidthHz), from + 1, levels.Length);
            double power = Math.Max(0, (sums[to] - sums[from]) / (to - from));
            return power > 0 ? 10 * Math.Log10(power) : double.NegativeInfinity;
        }

        SpectralDrop? best = null;
        for (int bin = (int)Math.Ceiling(8000 / BinWidthHz); bin * BinWidthHz < Nyquist - 1000; bin++)
        {
            double hz = bin * BinWidthHz;
            double before = BandLevel(hz - 700, hz - 100);
            double after = BandLevel(hz + 100, hz + 700);
            double tail = BandLevel(hz + 700, Nyquist);
            double drop = before - Math.Max(after, tail);
            if (before < -90 || drop < 20) continue;

            // Reject a single tone or a few isolated harmonics below an otherwise empty high-frequency region.
            int from = Math.Max(0, (int)((hz - 3500) / BinWidthHz));
            int to = Math.Max(from + 1, (int)((hz - 1000) / BinWidthHz));
            int populated = 0;
            for (int i = from; i < to; i++) if (levels[i] >= before - 20) populated++;
            if (populated < (to - from) * 0.6) continue;

            // Clamp only for reporting. An empty tail has an infinite numerical drop, not infinite precision.
            double reportedDrop = Math.Min(120, drop);
            if (best is null || reportedDrop > best.DropDb)
                best = new SpectralDrop(hz, reportedDrop);
        }
        return best;
    }
}
