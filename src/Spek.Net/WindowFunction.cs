namespace Spek.Net;

public enum WindowFunction
{
    Hann,
    Hamming,
    BlackmanHarris,
}

public static class WindowFunctions
{
    public static float[] Create(WindowFunction function, int size)
    {
        var window = new float[size];
        double step = 2 * Math.PI / (size - 1);
        for (int i = 0; i < size; i++)
        {
            double x = step * i;
            // Spek reads the 2x and 3x Blackman-Harris terms from its cosine table modulo n, which shifts them
            // slightly in the second half of the window. This uses the exact formula.
            window[i] = (float)(function switch
            {
                WindowFunction.Hann => 0.5 * (1 - Math.Cos(x)),
                WindowFunction.Hamming => 0.53836 - 0.46164 * Math.Cos(x),
                WindowFunction.BlackmanHarris =>
                    0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x),
                _ => throw new ArgumentOutOfRangeException(nameof(function)),
            });
        }
        return window;
    }

    public static string DisplayName(WindowFunction function) => function switch
    {
        WindowFunction.Hann => "Hann",
        WindowFunction.Hamming => "Hamming",
        WindowFunction.BlackmanHarris => "Blackman-Harris",
        _ => function.ToString(),
    };
}
