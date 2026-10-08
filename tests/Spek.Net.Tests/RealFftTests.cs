namespace Spek.Net.Tests;

[TestClass]
public sealed class RealFftTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(8)]
    [DataRow(11)]
    public void PowerDb_MatchesDirectDft(int bits)
    {
        int n = 1 << bits;
        var random = new Random(bits);
        var input = new float[n];
        for (int i = 0; i < n; i++)
        {
            input[i] = (float)(random.NextDouble() * 2 - 1);
        }

        var fft = new RealFft(bits);
        var actual = new float[fft.Bins];
        fft.PowerDb(input, actual);

        for (int k = 0; k <= n / 2; k++)
        {
            double re = 0, im = 0;
            for (int t = 0; t < n; t++)
            {
                double angle = -2 * Math.PI * k * t / n;
                re += input[t] * Math.Cos(angle);
                im += input[t] * Math.Sin(angle);
            }
            double expected = 10 * Math.Log10((re * re + im * im) / ((double)n * n));
            Assert.AreEqual(expected, actual[k], 0.01, $"bin {k} of {n}");
        }
    }

    [TestMethod]
    public void PowerDb_PutsAToneInItsBin()
    {
        const int bits = 11, n = 1 << bits, bin = 100;
        var input = new float[n];
        for (int i = 0; i < n; i++)
        {
            input[i] = (float)Math.Sin(2 * Math.PI * bin * i / n);
        }

        var output = new float[n / 2 + 1];
        new RealFft(bits).PowerDb(input, output);

        Assert.AreEqual(bin, Array.IndexOf(output, output.Max()));
        // A full-scale sine on a bin has |X| = n/2, so the power is 10 * log10(1/4).
        Assert.AreEqual(10 * Math.Log10(0.25), output[bin], 0.001);
    }
}
