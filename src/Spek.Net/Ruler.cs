namespace Spek.Net;

public readonly record struct RulerTick(int Value, double Position);
public readonly record struct TimeRulerTick(double Seconds, double Position);

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

    public static List<TimeRulerTick> TimeTicks(TimeRange range, double width, double labelWidth)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(labelWidth) || labelWidth <= 0) return [];
        double required = range.Span * labelWidth * 1.5 / width;
        double[] steps = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300, 600, 1200, 1800, 3600];
        double step = steps.FirstOrDefault(value => value >= required);
        if (step == 0) step = Math.Ceiling(required / 3600) * 3600;
        var ticks = new List<TimeRulerTick> { new(range.Start, 0) };
        for (double time = Math.Ceiling(range.Start / step) * step; time < range.End; time += step)
        {
            double position = range.FractionAt(time) * width;
            if (position < labelWidth * 1.2 || width - position < labelWidth * 1.2) continue;
            ticks.Add(new(time, position));
        }
        if (width >= labelWidth * 1.2) ticks.Add(new(range.End, width));
        return ticks;
    }

    public static string FormatTime(double seconds, bool fractional)
    {
        if (!fractional) return FormatTime((int)seconds);
        long hundredths = (long)Math.Round(seconds * 100);
        return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
    }

    public static string FormatFrequency(int hertz) => hertz is > 0 and < 1000 ? $"{hertz} Hz" : $"{hertz / 1000.0:0.##} kHz";

    // The dB ruler counts up from -upper to -lower, so a unit of 120 is -120 dB.
    public static string FormatDensity(int unit) => $"{-unit} dB";
}
