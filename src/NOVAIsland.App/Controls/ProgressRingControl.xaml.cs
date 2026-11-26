using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NOVAIsland.App.Controls;

public sealed partial class ProgressRingControl : UserControl
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(ProgressRingControl),
        new PropertyMetadata(1.0, OnVisualChanged));

    public static readonly DependencyProperty RingSizeProperty = DependencyProperty.Register(
        nameof(RingSize),
        typeof(double),
        typeof(ProgressRingControl),
        new PropertyMetadata(112.0, OnVisualChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(ProgressRingControl),
        new PropertyMetadata(6.0, OnVisualChanged));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public ProgressRingControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRingControl ring)
        {
            ring.Redraw();
        }
    }

    private void Redraw()
    {
        var size = Math.Max(20, RingSize);
        var stroke = Math.Max(2, StrokeThickness);
        var radius = (size - stroke) / 2.0;
        var center = size / 2.0;
        var ratio = Math.Clamp(Progress, 0.0, 0.9999);

        if (ratio <= 0.001)
        {
            ProgressArc.Data = null;
            return;
        }

        var angleRad = (ratio * 360.0 - 90.0) * Math.PI / 180.0;
        var startPoint = new Point(center, center - radius);
        var endPoint = new Point(
            center + radius * Math.Cos(angleRad),
            center + radius * Math.Sin(angleRad));

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = ratio > 0.5,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ProgressArc.Data = geometry;
    }
}
