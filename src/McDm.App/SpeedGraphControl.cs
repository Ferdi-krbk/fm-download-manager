using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace McDm.App;

public sealed class SpeedGraphControl : FrameworkElement
{
    private const int HistoryCount = 60; // 30 seconds of 500ms samples
    private readonly double[] _history = new double[HistoryCount];
    private int _head = 0;
    private double _peakSpeed = 0;
    private double _smoothedSpeed = 0;

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SpeedGraphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xC9, 0x1D, 0x3A)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(SpeedGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public SpeedGraphControl()
    {
        ClipToBounds = true;
    }

    public void PushSample(double speedBytesPerSec)
    {
        double val = Math.Max(0, speedBytesPerSec);
        
        // Exponential Moving Average filter (alpha = 0.3) removes momentary connection handover jitter
        if (_smoothedSpeed <= 0)
        {
            _smoothedSpeed = val;
        }
        else
        {
            _smoothedSpeed = (_smoothedSpeed * 0.80) + (val * 0.20);
            if (_smoothedSpeed < 1024 && val <= 0) _smoothedSpeed = 0;
        }

        _history[_head] = _smoothedSpeed;
        _head = (_head + 1) % HistoryCount;

        double max = 0;
        for (int i = 0; i < HistoryCount; i++)
        {
            if (_history[i] > max) max = _history[i];
        }
        _peakSpeed = max;

        InvalidateVisual();
    }

    public void Clear()
    {
        Array.Clear(_history, 0, _history.Length);
        _head = 0;
        _peakSpeed = 0;
        _smoothedSpeed = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        // Reorder samples chronologically (oldest to newest)
        var samples = new double[HistoryCount];
        for (int i = 0; i < HistoryCount; i++)
        {
            samples[i] = _history[(_head + i) % HistoryCount];
        }

        double maxScale = Math.Max(_peakSpeed * 1.15, 1024 * 512); // minimum 512 KB/s scale
        double stepX = w / (HistoryCount - 1);

        var pts = new Point[HistoryCount];
        for (int i = 0; i < HistoryCount; i++)
        {
            double x = i * stepX;
            double y = h - (samples[i] / maxScale * (h - 8));
            y = Math.Clamp(y, 4, h - 2);
            pts[i] = new Point(x, y);
        }

        // Build smooth cubic Bezier path
        var figure = new PathFigure { StartPoint = pts[0] };
        var fillFigure = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
        fillFigure.Segments.Add(new LineSegment(pts[0], true));

        for (int i = 1; i < HistoryCount; i++)
        {
            var p0 = pts[i - 1];
            var p1 = pts[i];
            double dx = (p1.X - p0.X) * 0.5;
            var cp1 = new Point(p0.X + dx, p0.Y);
            var cp2 = new Point(p1.X - dx, p1.Y);

            var bz = new BezierSegment(cp1, cp2, p1, true);
            figure.Segments.Add(bz);
            fillFigure.Segments.Add(bz);
        }

        fillFigure.Segments.Add(new LineSegment(new Point(w, h), true));

        // Fill Geometry
        var fillGeo = new PathGeometry();
        fillGeo.Figures.Add(fillFigure);

        // Dark Crimson gradient fill under the line
        var effectiveFill = FillBrush;
        if (effectiveFill == null)
        {
            var gb = new LinearGradientBrush(
                Color.FromArgb(0x45, 0xC9, 0x1D, 0x3A),
                Color.FromArgb(0x02, 0x0C, 0x0D, 0x12),
                new Point(0, 0),
                new Point(0, 1));
            gb.Freeze();
            effectiveFill = gb;
        }
        dc.DrawGeometry(effectiveFill, null, fillGeo);

        // Line Geometry
        var lineGeo = new PathGeometry();
        lineGeo.Figures.Add(figure);
        var linePen = new Pen(LineBrush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, lineGeo);

    }
}
