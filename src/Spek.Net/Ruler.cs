namespace Spek.Net;

public readonly record struct RulerTick(int Value, double Position);

// Spek's ruler: picks the smallest step whose labels fit, then lists the ticks with their offsets in pixels.
public static class Ruler
{
    public static readonly int[] TimeSteps = [1, 2, 5, 10, 20, 30, 60, 120, 300, 600, 1200, 1800];
    public static readonly int[] FrequencySteps = [1000, 2000, 5000, 10000, 20000];
    public static readonly int[] DensitySteps = [1, 2, 5, 10, 20, 50];

    // labelLength is the width of a sample label on a horizontal ruler, or its height on a vertical one. Vertical
    // rulers count up from the bottom, so their positions run from maxUnits at the top to minUnits at the bottom.
    public static List<RulerTick> Ticks(
        int minUnits, int maxUnits, IReadOnlyList<int> steps, double spacing, double scale, double offset, double labelLength, bool vertical)
    {
        int step = 0;
        foreach (int candidate in steps)
        {
            if (Math.Abs(scale * candidate) >= spacing * labelLength)
            {
                step = candidate;
                break;
            }
        }

        var ticks = new List<RulerTick> { Tick(minUnits), Tick(maxUnits) };
        if (step > 0)
        {
            for (int tick = minUnits + step; tick < maxUnits; tick += step)
            {
                if (Math.Abs(scale * (maxUnits - tick)) < labelLength * 1.2) break;
                ticks.Add(Tick(tick));
            }
        }
        return ticks;

        RulerTick Tick(int tick)
        {
            int value = vertical ? maxUnits + minUnits - tick : tick;
            return new RulerTick(tick, offset + scale * (value - minUnits));
        }
    }

    public static string FormatTime(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    public static string FormatFrequency(int hertz) => hertz is > 0 and < 1000 ? $"{hertz} Hz" : $"{hertz / 1000.0:0.##} kHz";

    // The dB ruler counts up from -upper to -lower, so a unit of 120 is -120 dB.
    public static string FormatDensity(int unit) => $"{-unit} dB";
}
