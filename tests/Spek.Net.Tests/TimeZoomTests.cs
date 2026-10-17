namespace Spek.Net.Tests;

[TestClass]
public sealed class TimeZoomTests
{
    [TestMethod]
    public void TimeCoordinatesUseAbsoluteFileSeconds()
    {
        var range = new TimeRange(30, 60);
        Assert.AreEqual(30, range.TimeAt(0), 1e-9);
        Assert.AreEqual(60, range.TimeAt(1), 1e-9);
        Assert.AreEqual(36, range.TimeAt(0.2), 1e-9);
        Assert.AreEqual(0.2, range.FractionAt(36), 1e-9);
        Assert.AreEqual(30, range.TimeAt(-1), 1e-9);
        Assert.AreEqual(60, range.TimeAt(2), 1e-9);
        Assert.AreEqual(-1, range.FractionAt(0), 1e-9);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.25)]
    [DataRow(0.5)]
    [DataRow(0.9)]
    [DataRow(1.0)]
    public void BothAxesKeepThePointUnderThePointer(double fraction)
    {
        var time = new TimeRange(0, 180);
        var frequency = new FrequencyRange(0, 24000);
        double seconds = time.TimeAt(fraction);
        double hz = frequency.FrequencyAt(fraction);
        var nextTime = time.Zoom(2, seconds, 180);
        var nextFrequency = frequency.Zoom(2, hz, 24000);
        Assert.AreEqual(90, nextTime.Span, 1e-9);
        Assert.AreEqual(12000, nextFrequency.Span, 1e-9);
        Assert.AreEqual(fraction, nextTime.FractionAt(seconds), 1e-9);
        Assert.AreEqual(fraction, nextFrequency.FractionFromTop(hz), 1e-9);
    }

    [TestMethod]
    public void CenterZoomPreservesTheVisibleCenter()
    {
        var range = new TimeRange(20, 80);
        var next = range.Zoom(2, range.TimeAt(0.5), 100);
        Assert.AreEqual(new TimeRange(35, 65), next);
        Assert.AreEqual(range, next.Zoom(0.5, next.TimeAt(0.5), 100));
    }

    [TestMethod]
    public void TimeZoomStopsAtTheMinimumSpanAndRestoresTheWholeFile()
    {
        var range = new TimeRange(0, 180);
        for (int i = 0; i < 100; i++) range = range.Zoom(2, 180, 180, minimumSpan: 0.5);
        Assert.AreEqual(0.5, range.Span, 1e-9);
        Assert.AreEqual(180, range.End, 1e-9);
        Assert.AreEqual(new TimeRange(0, 180), range.Zoom(0.000001, 180, 180));
    }

    [TestMethod]
    public void ShortFilesCanUseTheirWholeDuration()
    {
        Assert.AreEqual(new TimeRange(0, 0.005), new TimeRange(0, 0.005).Zoom(2, 0.0025, 0.005));
    }

    [TestMethod]
    public void SelectionEndpointsMapIntoTheZoomedPassageNotTheWholeFile()
    {
        var range = new TimeRange(30, 60);
        const double duration = 120;
        double start = range.TimeAt(0.2) / duration;
        double end = range.TimeAt(0.8) / duration;
        Assert.AreEqual(36, start * duration, 1e-9);
        Assert.AreEqual(54, end * duration, 1e-9);
        Assert.AreEqual(0.2, range.FractionAt(start * duration), 1e-9);
        Assert.AreEqual(0.8, range.FractionAt(end * duration), 1e-9);
    }

    [TestMethod]
    public void TimeRulerUsesTheVisibleIntervalWithFractionalLabels()
    {
        var ticks = Ruler.TimeTicks(new TimeRange(30.25, 32.75), 800, 60);
        Assert.AreEqual(new TimeRulerTick(30.25, 0), ticks[0]);
        Assert.AreEqual(new TimeRulerTick(32.75, 800), ticks[^1]);
        Assert.IsTrue(ticks.All(tick => tick.Seconds >= 30.25 && tick.Seconds <= 32.75));
        Assert.IsTrue(ticks.All(tick => tick.Position >= 0 && tick.Position <= 800));
        Assert.AreEqual("0:30.25", Ruler.FormatTime(30.25, true));
        Assert.AreEqual("1:00.00", Ruler.FormatTime(59.999, true));
        Assert.AreEqual("1:30", Ruler.FormatTime(90.5, false));
    }

    [TestMethod]
    public void InvalidTimeRangesAndZoomInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(0, double.NaN));
        var range = new TimeRange(0, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Zoom(0, 5, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Zoom(double.NaN, 5, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Zoom(2, double.NaN, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Zoom(2, 5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Zoom(2, 5, 10, 0));
    }
}
