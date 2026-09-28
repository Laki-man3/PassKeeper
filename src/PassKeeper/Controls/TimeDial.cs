using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PassKeeper.Controls;

public enum TimeUnit { Hours, Minutes, Seconds }

/// <summary>
/// Clock face for one part of a duration: 0–24 hours or 0–59 minutes/seconds. Drag the knob, click the face,
/// scroll the wheel or use the arrow keys. The other parts of <see cref="Value"/> are kept.
/// </summary>
public sealed class TimeDial : FrameworkElement
{
    public static readonly TimeSpan MaxValue = TimeSpan.FromHours(24);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TimeSpan), typeof(TimeDial),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, v) => Clamp((TimeSpan)v)));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(TimeUnit), typeof(TimeDial),
        new FrameworkPropertyMetadata(TimeUnit.Hours, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(TimeDial), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FaceBrushProperty = BrushProperty("FaceBrush");
    public static readonly DependencyProperty TrackBrushProperty = BrushProperty("TrackBrush");
    public static readonly DependencyProperty TickBrushProperty = BrushProperty("TickBrush");
    public static readonly DependencyProperty LabelBrushProperty = BrushProperty("LabelBrush");
    public static readonly DependencyProperty AccentBrushProperty = BrushProperty("AccentBrush");
    public static readonly DependencyProperty AccentSoftBrushProperty = BrushProperty("AccentSoftBrush");
    public static readonly DependencyProperty OnAccentBrushProperty = BrushProperty("OnAccentBrush");

    /// <summary>Raised when the user releases the knob (used to move on to the next unit).</summary>
    public event EventHandler? DragCompleted;

    public TimeDial()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        SetResourceReference(FaceBrushProperty, "Brush.SurfaceAlt");
        SetResourceReference(TrackBrushProperty, "Brush.Border");
        SetResourceReference(TickBrushProperty, "Brush.BorderStrong");
        SetResourceReference(LabelBrushProperty, "Brush.TextSecondary");
        SetResourceReference(AccentBrushProperty, "Brush.Accent");
        SetResourceReference(AccentSoftBrushProperty, "Brush.AccentSoft");
        SetResourceReference(OnAccentBrushProperty, "Brush.OnAccent");
        IsKeyboardFocusedChanged += (_, _) => InvalidateVisual();
    }

    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public TimeUnit Unit
    {
        get => (TimeUnit)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private Brush B(DependencyProperty p) => (Brush)GetValue(p);

    public static TimeSpan Clamp(TimeSpan value)
    {
        value = TimeSpan.FromSeconds(Math.Round(value.TotalSeconds));
        return value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxValue ? MaxValue : value;
    }

    public static int Part(TimeSpan value, TimeUnit unit) => unit switch
    {
        TimeUnit.Hours => (int)value.TotalHours,
        TimeUnit.Minutes => value.Minutes,
        _ => value.Seconds,
    };

    /// <summary>Replaces one part of the duration; 24 hours forces minutes and seconds to zero.</summary>
    public static TimeSpan WithPart(TimeSpan value, TimeUnit unit, int part)
    {
        int h = (int)value.TotalHours, m = value.Minutes, s = value.Seconds;
        switch (unit)
        {
            case TimeUnit.Hours: h = Math.Clamp(part, 0, 24); break;
            case TimeUnit.Minutes: m = Math.Clamp(part, 0, 59); break;
            default: s = Math.Clamp(part, 0, 59); break;
        }
        if (h == 24) m = s = 0;
        return new TimeSpan(h, m, s);
    }

    private int Positions => Unit == TimeUnit.Hours ? 24 : 60;

    private void Step(int delta)
    {
        var max = Unit == TimeUnit.Hours ? 24 : 59;
        var next = Part(Value, Unit) + delta;
        if (next > max) next = 0;
        else if (next < 0) next = max;
        Value = WithPart(Value, Unit, next);
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SetFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        DragCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Step(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var big = Unit == TimeUnit.Hours ? 1 : 5;
        switch (e.Key)
        {
            case Key.Up or Key.Right: Step(1); break;
            case Key.Down or Key.Left: Step(-1); break;
            case Key.PageUp: Step(big); break;
            case Key.PageDown: Step(-big); break;
            case Key.Home: Value = WithPart(Value, Unit, 0); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SetFromPoint(Point p)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        if (dx * dx + dy * dy < 16) return; // the centre has no direction
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var index = (int)Math.Round(angle / (360.0 / Positions)) % Positions;
        // The top of the hour face is both 0 and 24: coming from the evening side means 24.
        if (Unit == TimeUnit.Hours && index == 0 && Part(Value, TimeUnit.Hours) >= 18) index = 24;
        Value = WithPart(Value, Unit, index);
    }

    // ------------------------------------------------------------------ rendering

    private static Point OnCircle(Point c, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + radius * Math.Sin(rad), c.Y - radius * Math.Cos(rad));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsInfinity(availableSize.Width) ? 200 : Math.Min(availableSize.Width, 200);
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 60) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2 - 1;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var accent = B(AccentBrushProperty);

        dc.DrawEllipse(B(FaceBrushProperty), new Pen(IsKeyboardFocused ? accent : B(TrackBrushProperty), 1), c, outer, outer);

        var positions = Positions;
        var part = Part(Value, Unit);
        var step = 360.0 / positions;
        var valueAngle = Unit == TimeUnit.Hours && part == 24 ? 360 : part * step;

        // Rim: track + filled arc from 12 o'clock to the value.
        var rim = outer - 7;
        dc.DrawEllipse(null, new Pen(B(TrackBrushProperty), 4), c, rim, rim);
        if (valueAngle > 0)
        {
            var arcPen = new Pen(accent, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (valueAngle >= 359.99) dc.DrawEllipse(null, arcPen, c, rim, rim);
            else
            {
                var fig = new PathFigure { StartPoint = OnCircle(c, rim, 0), IsClosed = false };
                fig.Segments.Add(new ArcSegment(OnCircle(c, rim, valueAngle), new Size(rim, rim), 0, valueAngle > 180, SweepDirection.Clockwise, true));
                dc.DrawGeometry(null, arcPen, new PathGeometry([fig]));
            }
        }

        // Ticks and labels.
        var tickPen = new Pen(B(TickBrushProperty), 1);
        var labelEvery = Unit == TimeUnit.Hours ? 2 : 5;
        var labelRadius = outer - 30;
        for (var i = 0; i < positions; i++)
        {
            var a = i * step;
            var major = i % labelEvery == 0;
            dc.DrawLine(tickPen, OnCircle(c, rim - 7, a), OnCircle(c, rim - (major ? 12 : 9), a));
            if (!major) continue;
            var text = new FormattedText(i.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 11.5, B(LabelBrushProperty), dpi);
            var at = OnCircle(c, labelRadius, a);
            dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }

        // Hand with the knob showing the selected value.
        var knob = OnCircle(c, labelRadius, valueAngle);
        dc.DrawLine(new Pen(accent, 2), c, knob);
        dc.DrawEllipse(accent, null, c, 3.5, 3.5);
        dc.DrawEllipse(accent, new Pen(B(AccentSoftBrushProperty), 6), knob, 15, 15);
        var knobText = new FormattedText(part.ToString("00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, B(OnAccentBrushProperty), dpi);
        dc.DrawText(knobText, new Point(knob.X - knobText.Width / 2, knob.Y - knobText.Height / 2));
    }
}
