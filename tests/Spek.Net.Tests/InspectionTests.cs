namespace Spek.Net.Tests;

[TestClass]
public sealed class InspectionTests
{
    [TestMethod]
    public void FrequencyRangeMapsTopAndBottomAndRoundTrips()
    {
        var range = new FrequencyRange(15000, 22050);
        Assert.AreEqual(22050, range.FrequencyAt(0), 1e-9);
        Assert.AreEqual(15000, range.FrequencyAt(1), 1e-9);
        Assert.AreEqual(18000, range.FrequencyAt(range.FractionFromTop(18000)), 1e-9);
    }

    [TestMethod]
    public void ZoomKeepsTheFrequencyUnderThePointer()
    {
        var range = new FrequencyRange(0, 24000);
        double fraction = range.FractionFromTop(18000);
        var zoomed = range.Zoom(2, 18000, 24000);
        Assert.AreEqual(12000, zoomed.Span, 1e-9);
        Assert.AreEqual(fraction, zoomed.FractionFromTop(18000), 1e-9);
    }

    [TestMethod]
    public void ZoomStaysInTheStreamAndHasAMinimumSpan()
    {
        var range = new FrequencyRange(0, 22050);
        for (int i = 0; i < 100; i++) range = range.Zoom(2, 22050, 22050);
        Assert.AreEqual(100, range.Span, 1e-9);
        Assert.AreEqual(22050, range.Maximum, 1e-9);
        range = range.Zoom(0.0001, 22050, 22050);
        Assert.AreEqual(new FrequencyRange(0, 22050), range);
    }

    [TestMethod]
    public void InvalidFrequencyRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyRange(1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyRange(-1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyRange(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyRange(0, 1000).Zoom(0, 500, 1000));
    }

    [TestMethod]
    public void ProfileAveragesLinearPowerRatherThanDb()
    {
        var data = new Spectrogram(2, 3, Palette.Mono, -120, 0);
        data.SetColumn(0, [-10, -20, -30]);
        data.SetColumn(1, [-20, -30, -40]);
        var profile = data.GetProfile(0, 2, 48000);
        Assert.AreEqual(2, profile.IncludedColumns);
        Assert.AreEqual(12000, profile.BinWidthHz, 1e-9);
        Assert.AreEqual(10 * Math.Log10((0.1 + 0.01) / 2), profile.LevelsDb.Span[0], 1e-5);
    }

    [TestMethod]
    public void ProfileUsesThePassageAndIgnoresSilenceAndUnfinishedColumns()
    {
        var data = new Spectrogram(4, 3, Palette.Mono, -120, 0);
        data.SetColumn(0, [-20, -20, -20]);
        data.SetColumn(1, [float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity]);
        data.SetColumn(3, [-60, -60, -60]);
        var profile = data.GetProfile(0, 3, 44100);
        Assert.AreEqual(1, profile.IncludedColumns);
        Assert.AreEqual(-20, profile.LevelsDb.Span[0], 1e-5);
        Assert.AreEqual(-60, data.GetProfile(3, 4, 44100).LevelsDb.Span[0], 1e-5);
    }

    [TestMethod]
    public void ProfileSelectionIsValidatedAndCancelable()
    {
        var data = new Spectrogram(4, 1025, Palette.Mono, -120, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.GetProfile(0, 0, 44100));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.GetProfile(0, 5, 44100));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.GetValue(0, 1025));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => data.GetProfile(0, 4, 44100, cancel.Token));
    }

    [TestMethod]
    public void ASharpSustainedDropGetsAMeasuredMarker()
    {
        var profile = MakeProfile(hz => hz < 16000 ? -40 : -100);
        var result = profile.FindDrop();
        Assert.IsNotNull(result);
        Assert.AreEqual(16000, result.FrequencyHz, 400);
        Assert.IsTrue(result.DropDb >= 20);
    }

    [TestMethod]
    public void SilenceSparseTonesSmoothRolloffAndNarrowNotchesGetNoMarker()
    {
        Assert.IsNull(MakeProfile(_ => float.NegativeInfinity).FindDrop());
        Assert.IsNull(MakeProfile(hz => Math.Abs(hz - 10000) < 100 ? -20 : -130).FindDrop());
        Assert.IsNull(MakeProfile(hz => (float)(-40 - 60 * hz / 24000)).FindDrop());
        Assert.IsNull(MakeProfile(hz => hz is > 16000 and < 16500 ? -120 : -40).FindDrop());
        Assert.IsNull(MakeProfile(_ => -40).FindDrop());
    }

    [TestMethod]
    public void QuieterHighFrequenciesAreNotEnoughForAMarker()
    {
        Assert.IsNull(MakeProfile(hz => hz < 16000 ? -40 : -50).FindDrop());
        Assert.IsNull(MakeProfile(hz => hz < 16000 ? -100 : -140).FindDrop());
    }

    [TestMethod]
    public void EncoderReferencesAreExplicitLameDefaultsNotInferredBitrates()
    {
        CollectionAssert.AreEqual(new[] { 128, 192, 256, 320 }, EncoderReferences.Mp3Lame.Select(r => r.BitRateKbps).ToArray());
        CollectionAssert.AreEqual(new[] { 17000, 18600, 19700, 20500 }, EncoderReferences.Mp3Lame.Select(r => r.FrequencyHz).ToArray());
    }

    private static SpectrumProfile MakeProfile(Func<double, float> level)
    {
        const int bins = 2049;
        const double step = 48000.0 / (2 * (bins - 1));
        return new SpectrumProfile(Enumerable.Range(0, bins).Select(i => level(i * step)), step, 100);
    }
}
