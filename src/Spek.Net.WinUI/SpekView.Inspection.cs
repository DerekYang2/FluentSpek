using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Spek.Net.WinUI;

public sealed partial class SpekView
{
    public static readonly DependencyProperty ReferenceLinesProperty =
        Register(nameof(ReferenceLines), typeof(ReferenceLines), ReferenceLines.None, Redraw);
    public static readonly DependencyProperty ShowCutoffMarkerProperty =
        Register(nameof(ShowCutoffMarker), typeof(bool), false, InspectionOptionChanged);
    public static readonly DependencyProperty ShowSpectrumProperty =
        Register(nameof(ShowSpectrum), typeof(bool), false, InspectionOptionChanged);
    public static readonly DependencyProperty MinimumFrequencyProperty =
        Register(nameof(MinimumFrequency), typeof(double), 0.0, FrequencyChanged);
    public static readonly DependencyProperty MaximumFrequencyProperty =
        Register(nameof(MaximumFrequency), typeof(double), 0.0, FrequencyChanged);
    public static readonly DependencyProperty ReadoutTextProperty =
        Register(nameof(ReadoutText), typeof(string), "", null);

    private CancellationTokenSource? profileRun;
    private int profileGeneration;
    private double selectionStart;
    private double selectionEnd = 1;
    private double? dragAnchor;
    private double dragEnd;
    private double? keyboardSelectionAnchor;
    private uint dragPointer;
    private Point? cursor;
    private bool settingFrequencyRange;

    public ReferenceLines ReferenceLines { get => (ReferenceLines)GetValue(ReferenceLinesProperty); set => SetValue(ReferenceLinesProperty, value); }
    public bool ShowCutoffMarker { get => (bool)GetValue(ShowCutoffMarkerProperty); set => SetValue(ShowCutoffMarkerProperty, value); }
    public bool ShowSpectrum { get => (bool)GetValue(ShowSpectrumProperty); set => SetValue(ShowSpectrumProperty, value); }
    public double MinimumFrequency { get => (double)GetValue(MinimumFrequencyProperty); set => SetValue(MinimumFrequencyProperty, value); }

    // Zero selects the stream's Nyquist frequency automatically.
    public double MaximumFrequency { get => (double)GetValue(MaximumFrequencyProperty); set => SetValue(MaximumFrequencyProperty, value); }
    public string ReadoutText { get => (string)GetValue(ReadoutTextProperty); private set => SetValue(ReadoutTextProperty, value); }

    public SpectrumProfile? SpectrumProfile { get; private set; }
    public SpectralDrop? MeasuredDrop { get; private set; }
    public double SelectionStartSeconds => selectionStart * (info?.Duration ?? 0);
    public double SelectionEndSeconds => selectionEnd * (info?.Duration ?? 0);
    public bool HasSelection => selectionStart > 0 || selectionEnd < 1;

    public FrequencyRange VisibleFrequencyRange
    {
        get
        {
            double ceiling = Math.Max(1, (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0);
            double max = MaximumFrequency;
            if (!double.IsFinite(max) || max <= 0) max = ceiling;
            max = Math.Clamp(max, Math.Min(100, ceiling), ceiling);
            double min = MinimumFrequency;
            if (!double.IsFinite(min)) min = 0;
            min = Math.Clamp(min, 0, max - Math.Min(100, ceiling));
            return new FrequencyRange(min, max);
        }
    }

    // Fires for a new profile, time selection or frequency zoom. Hover text uses a dependency property separately.
    public event EventHandler? InspectionChanged;

    public void ResetFrequencyZoom()
    {
        SetFrequencyRange(0, 0);
    }

    public void ZoomToFrequencyRange(double minimum, double maximum)
    {
        var range = new FrequencyRange(minimum, maximum);
        double ceiling = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        if (ceiling <= 0 || range.Maximum > ceiling || range.Span < Math.Min(100, ceiling))
            throw new ArgumentOutOfRangeException(nameof(maximum), "The interval must fit the stream and span at least 100 Hz.");
        SetFrequencyRange(range.Minimum, range.Maximum);
    }

    public void SelectTimeRange(double startSeconds, double endSeconds)
    {
        double duration = info?.Duration ?? 0;
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || duration <= 0
            || startSeconds < 0 || endSeconds <= startSeconds || endSeconds > duration)
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "The time range must fit within the file.");
        SetSelection(startSeconds / duration, endSeconds / duration);
    }

    public void ClearSelection()
    {
        keyboardSelectionAnchor = null;
        SetSelection(0, 1);
    }

    private void InitializeInspection()
    {
        AutomationProperties.SetAutomationId(this, "Spectrogram");
        AutomationProperties.SetName(this, "Spectrogram. Use arrow keys to inspect, Shift and left or right to select time, plus and minus to zoom, R to reset zoom, Escape to clear selection.");
        PointerMoved += OnInspectionPointerMoved;
        PointerExited += (_, _) => { if (dragAnchor is null) { cursor = null; ReadoutText = ""; canvas.Invalidate(); } };
        PointerReleased += OnSelectionReleased;
        PointerCanceled += (_, _) => CancelDrag();
        PointerCaptureLost += (_, _) => CancelDrag();
        PointerWheelChanged += OnFrequencyWheel;
        DoubleTapped += (_, _) => ResetFrequencyZoom();
    }

    private static void InspectionOptionChanged(SpekView view)
    {
        _ = view.RefreshProfileAsync();
        view.canvas.Invalidate();
    }

    private static void FrequencyChanged(SpekView view)
    {
        if (view.settingFrequencyRange) return;
        view.canvas.Invalidate();
        view.InspectionChanged?.Invoke(view, EventArgs.Empty);
    }

    private void SetFrequencyRange(double minimum, double maximum)
    {
        settingFrequencyRange = true;
        MinimumFrequency = minimum;
        MaximumFrequency = maximum;
        settingFrequencyRange = false;
        UpdateReadout();
        canvas.Invalidate();
        InspectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelection(double start, double end)
    {
        selectionStart = Math.Clamp(Math.Min(start, end), 0, 1);
        selectionEnd = Math.Clamp(Math.Max(start, end), 0, 1);
        canvas.Invalidate();
        _ = RefreshProfileAsync();
    }

    private void BeginSelection(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (!point.Properties.IsLeftButtonPressed || !plot.Contains(point.Position) || spectrogram is null) return;
        dragAnchor = Math.Clamp((point.Position.X - plot.Left) / plot.Width, 0, 1);
        keyboardSelectionAnchor = null;
        dragEnd = dragAnchor.Value;
        dragPointer = point.PointerId;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnInspectionPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        cursor = plot.Contains(point.Position) ? point.Position : null;
        if (dragAnchor is not null && point.PointerId == dragPointer && plot.Width > 0)
            dragEnd = Math.Clamp((point.Position.X - plot.Left) / plot.Width, 0, 1);
        UpdateReadout();
        canvas.Invalidate();
    }

    private void OnSelectionReleased(object sender, PointerRoutedEventArgs e)
    {
        if (dragAnchor is not double start || e.GetCurrentPoint(this).PointerId != dragPointer) return;
        double end = dragEnd;
        dragAnchor = null;
        ReleasePointerCapture(e.Pointer);
        double pixelWidth = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight)).Width;
        if (Math.Abs(end - start) * pixelWidth >= 3) SetSelection(start, end);
        e.Handled = true;
    }

    private void CancelDrag()
    {
        dragAnchor = null;
        canvas.Invalidate();
    }

    private void OnFrequencyWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDown(VirtualKey.Control) || spectrogram is null) return;
        var point = e.GetCurrentPoint(this);
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (!plot.Contains(point.Position)) return;
        double anchor = VisibleFrequencyRange.FrequencyAt((point.Position.Y - plot.Top) / plot.Height);
        double factor = Math.Pow(1.25, point.Properties.MouseWheelDelta / 120.0);
        ZoomFrequency(factor, anchor);
        e.Handled = true;
    }

    private void ZoomFrequency(double factor, double? anchor = null)
    {
        double ceiling = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        if (ceiling <= 0) return;
        var current = VisibleFrequencyRange;
        var next = current.Zoom(factor, anchor ?? (current.Minimum + current.Maximum) / 2, ceiling);
        SetFrequencyRange(next.Minimum, next.Maximum);
    }

    private bool HandleInspectionKey(KeyRoutedEventArgs e)
    {
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) return false;
        switch (e.Key)
        {
            case VirtualKey.R: ResetFrequencyZoom(); break;
            case VirtualKey.Escape:
                CancelDrag();
                ReleasePointerCaptures();
                ClearSelection();
                break;
            // Windows.System.VirtualKey leaves the OEM plus and minus keys unnamed.
            case VirtualKey.Add or (VirtualKey)0xBB: ZoomFrequency(1.5); break;
            case VirtualKey.Subtract or (VirtualKey)0xBD: ZoomFrequency(1 / 1.5); break;
            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down:
                MoveInspectionCursor(e.Key, IsDown(VirtualKey.Shift));
                break;
            default: return false;
        }
        e.Handled = true;
        return true;
    }

    private void MoveInspectionCursor(VirtualKey key, bool extend)
    {
        if (spectrogram is null) return;
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        var old = cursor ?? new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
        double x = old.X, y = old.Y;
        if (key == VirtualKey.Left) x -= Math.Max(1, plot.Width / spectrogram.Columns);
        if (key == VirtualKey.Right) x += Math.Max(1, plot.Width / spectrogram.Columns);
        if (key == VirtualKey.Up) y -= 1;
        if (key == VirtualKey.Down) y += 1;
        cursor = new Point(Math.Clamp(x, plot.Left, plot.Right), Math.Clamp(y, plot.Top, plot.Bottom));
        if (extend && key is VirtualKey.Left or VirtualKey.Right)
        {
            keyboardSelectionAnchor ??= (old.X - plot.Left) / plot.Width;
            SetSelection(keyboardSelectionAnchor.Value, (cursor.Value.X - plot.Left) / plot.Width);
        }
        else keyboardSelectionAnchor = null;
        UpdateReadout();
        canvas.Invalidate();
    }

    private void UpdateReadout()
    {
        if (cursor is not Point point || spectrogram is null) { ReadoutText = ""; return; }
        var plot = SpekRenderer.PlotRect(new Size(ActualWidth, ActualHeight));
        if (plot.Width <= 0 || plot.Height <= 0) return;
        double fraction = Math.Clamp((point.X - plot.Left) / plot.Width, 0, 1);
        double hz = VisibleFrequencyRange.FrequencyAt((point.Y - plot.Top) / plot.Height);
        int x = Math.Clamp((int)(fraction * spectrogram.Columns), 0, spectrogram.Columns - 1);
        double nyquist = (info?.GetStream(Stream)?.SampleRate ?? 0) / 2.0;
        int bin = nyquist > 0 ? Math.Clamp((int)Math.Round(hz / nyquist * (spectrogram.Bins - 1)), 0, spectrogram.Bins - 1) : 0;
        double time = fraction * (info?.Duration ?? 0);
        string level = spectrogram.HasColumn(x) ? $"{spectrogram.GetValue(x, bin):0.0} dB" : "not analysed yet";
        ReadoutText = $"{(int)(time / 60)}:{time % 60:00.000}   {hz:0} Hz   {level}";
    }

    private void CancelProfile()
    {
        profileRun?.Cancel();
        profileRun?.Dispose();
        profileRun = null;
        profileGeneration++;
    }

    private void ResetProfile()
    {
        CancelProfile();
        SpectrumProfile = null;
        MeasuredDrop = null;
        cursor = null;
        ReadoutText = "";
        InspectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshProfileAsync()
    {
        CancelProfile();
        SpectrumProfile = null;
        MeasuredDrop = null;
        InspectionChanged?.Invoke(this, EventArgs.Empty);
        var data = spectrogram;
        int rate = info?.GetStream(Stream)?.SampleRate ?? 0;
        if (!loaded || IsAnalyzing || data is null || rate <= 0 || (!ShowSpectrum && !ShowCutoffMarker)) return;

        int version = profileGeneration;
        var token = (profileRun = new CancellationTokenSource()).Token;
        int first = Math.Clamp((int)(selectionStart * data.Columns), 0, data.Columns - 1);
        int end = Math.Clamp((int)Math.Ceiling(selectionEnd * data.Columns), first + 1, data.Columns);
        try
        {
            var result = await Task.Run(() =>
            {
                var profile = data.GetProfile(first, end, rate, token);
                return (Profile: profile, Drop: profile.FindDrop());
            }, token);
            if (version != profileGeneration || !loaded) return;
            SpectrumProfile = result.Profile;
            MeasuredDrop = result.Drop;
            canvas.Invalidate();
            InspectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    private void DrawInspection(CanvasDrawingSession ds, Size size)
    {
        var plot = SpekRenderer.PlotRect(size);
        if (plot.Width <= 0 || plot.Height <= 0) return;
        using var clip = ds.CreateLayer(1, plot);
        double first = dragAnchor ?? selectionStart, end = dragAnchor is null ? selectionEnd : dragEnd;
        if (dragAnchor is not null || HasSelection)
        {
            var selected = new Rect(plot.Left + Math.Min(first, end) * plot.Width, plot.Top,
                Math.Abs(end - first) * plot.Width, plot.Height);
            ds.FillRectangle(selected, Color.FromArgb(30, 255, 255, 255));
            ds.DrawRectangle(selected, Colors.White);
        }
        if (cursor is Point point)
        {
            ds.DrawLine((float)plot.Left, (float)point.Y, (float)plot.Right, (float)point.Y, Colors.Black, 3);
            ds.DrawLine((float)plot.Left, (float)point.Y, (float)plot.Right, (float)point.Y, Colors.White);
            ds.DrawLine((float)point.X, (float)plot.Top, (float)point.X, (float)plot.Bottom, Colors.Black, 3);
            ds.DrawLine((float)point.X, (float)plot.Top, (float)point.X, (float)plot.Bottom, Colors.White);
        }
    }
}
