namespace Spek.Net.Tests;

[TestClass]
public sealed class WindowAndPaletteTests
{
    [TestMethod]
    public void Windows_AreSymmetricWithTheExpectedEnds()
    {
        const int n = 2048;
        var hann = WindowFunctions.Create(WindowFunction.Hann, n);
        var hamming = WindowFunctions.Create(WindowFunction.Hamming, n);
        var blackmanHarris = WindowFunctions.Create(WindowFunction.BlackmanHarris, n);

        Assert.AreEqual(0, hann[0], 1e-6);
        Assert.AreEqual(0.07672, hamming[0], 1e-5);
        Assert.AreEqual(0.00006, blackmanHarris[0], 1e-5);
        foreach (var window in new[] { hann, hamming, blackmanHarris })
        {
            for (int i = 0; i < n / 2; i++)
            {
                Assert.AreEqual(window[i], window[n - 1 - i], 1e-5);
            }
            Assert.AreEqual(1, window.Max(), 0.001);
        }
    }

    [TestMethod]
    [DataRow(Palette.Mono, 0.0, 0x000000u)]
    [DataRow(Palette.Mono, 0.5, 0x808080u)]
    [DataRow(Palette.Mono, 1.0, 0xFFFFFFu)]
    [DataRow(Palette.Spectrum, 0.0, 0x000000u)]
    [DataRow(Palette.Spectrum, 0.5, 0x09FF00u)]
    [DataRow(Palette.Spectrum, 1.0, 0xFF0000u)]
    [DataRow(Palette.Sox, 0.0, 0x000000u)]
    [DataRow(Palette.Sox, 1.0, 0xFFFFFFu)]
    public void Palettes_MatchSpek(Palette palette, double level, uint expected)
    {
        Assert.AreEqual(expected.ToString("X6"), Palettes.Map(palette, level).ToString("X6"));
    }

    [TestMethod]
    public void Spectrum_IsNeverBlackAboveTheBottom()
    {
        for (int i = 1; i <= 1000; i++)
        {
            Assert.AreNotEqual(0u, Palettes.Map(Palette.Spectrum, i / 1000.0), $"level {i / 1000.0}");
        }
    }
}
