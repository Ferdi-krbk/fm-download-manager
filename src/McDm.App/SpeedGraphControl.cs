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

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(SpeedGraphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xC9, 0x2A, 0x3E)), FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public SpeedGraphControl()
    {
        ClipToBounds = true;
    }

    public void PushSample(double speedBytesPerSec)
    {
        _history[_head] = Math.Max(0, speedBytesPerSec);
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
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        // Subtle background grid lines (horizontal 25%, 50%, 75%)
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 1);
        gridPen.Freeze();
        dc.DrawLine(gridPen, new Point(0, h * 0.25), new Point(w, h * 0.25));
        dc.DrawLine(gridPen, new Point(0, h * 0.50), new Point(w, h * 0.50));
        dc.DrawLine(gridPen, new Point(0, h * 0.75), new Point(w, h * 0.75));

        // Reorder samples chronologically (oldest to newest)
        var samples = new double[HistoryCount];
        for (int i = 0; i < HistoryCount; i++)
        {
            samples[i] = _history[(_head + i) % HistoryCount];
        }

        double maxScale = Math.Max(_peakSpeed * 1.15, 1024 * 512); // minimum 512 KB/s scale

        // Build geometric path
        var figure = new PathFigure();
        double stepX = w / (HistoryCount - 1);

        double firstY = h - (samples[0] / maxScale * (h - 6));
        firstY = Math.Clamp(firstY, 4, h - 2);
        figure.StartPoint = new Point(0, firstY);

        var points = new PointCollection(HistoryCount);
        for (int i = 1; i < HistoryCount; i++)
        {
            double x = i * stepX;
            double y = h - (samples[i] / maxScale * (h - 6));
            y = Math.Clamp(y, 4, h - 2);
            points.Add(new Point(x, y));
        }

        figure.Segments.Add(new PolyLineSegment(points, true));

        // Create fill geometry (closed to bottom)
        var fillFigure = new PathFigure
        {
            StartPoint = new Point(0, h),
            IsClosed = true,
        };
        fillFigure.Segments.Add(new LineSegment(new Point(0, firstY), true));
        fillFigure.Segments.Add(new PolyLineSegment(points, true));
        fillFigure.Segments.Add(new LineSegment(new Point(w, h), true));

        var fillGeo = new PathGeometry();
        fillGeo.Figures.Add(fillFigure);

        // Burgundy gradient fill under the line
        var fillBrush = new LinearGradientBrush(
            Color.FromArgb(0x55, 0xC9, 0x2A, 0x3E),
            Color.FromArgb(0x05, 0x1E, 0x10, 0x15),
            new Point(0, 0),
            new Point(0, 1));
        fillBrush.Freeze();
        dc.DrawGeometry(fillBrush, null, fillGeo);

        // Draw line on top
        var lineGeo = new PathGeometry();
        lineGeo.Figures.Add(figure);
        var linePen = new Pen(LineBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, lineGeo);

        // Peak speed label in top-right corner
        if (_peakSpeed > 0)
        {
            string peakText = "Pik: " + BytesConverter.Format(_peakSpeed) + "/s";
            var ft = new FormattedText(
                peakText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Bahnschrift, Segoe UI, sans-serif"),
                10,
                new SolidColorBrush(Color.FromArgb(0x99, 0x9C, 0xA3, 0xAF)),
                1.0);
            dc.DrawText(ft, new Point(w - ft.Width - 6, 4));
        }
    }
}
