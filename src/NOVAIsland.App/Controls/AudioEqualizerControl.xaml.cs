using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace NOVAIsland.App.Controls;

public sealed partial class AudioEqualizerControl : UserControl
{
    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount),
        typeof(int),
        typeof(AudioEqualizerControl),
        new PropertyMetadata(5, OnStructureChanged));

    public static readonly DependencyProperty MaxBarHeightProperty = DependencyProperty.Register(
        nameof(MaxBarHeight),
        typeof(double),
        typeof(AudioEqualizerControl),
        new PropertyMetadata(16.0, OnStructureChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(AudioEqualizerControl),
        new PropertyMetadata(false, OnLevelsChanged));

    public static readonly DependencyProperty LevelsProperty = DependencyProperty.Register(
        nameof(Levels),
        typeof(double[]),
        typeof(AudioEqualizerControl),
        new PropertyMetadata(null, OnLevelsChanged));

    private readonly List<Visual> _barVisuals = [];

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public double MaxBarHeight
    {
        get => (double)GetValue(MaxBarHeightProperty);
        set => SetValue(MaxBarHeightProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double[]? Levels
    {
        get => (double[]?)GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    public AudioEqualizerControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RebuildBars();
    }

    private static void OnStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioEqualizerControl eq)
        {
            eq.RebuildBars();
        }
    }

    private static void OnLevelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioEqualizerControl eq)
        {
            eq.UpdateBars();
        }
    }

    private void RebuildBars()
    {
        BarsHost.Children.Clear();
        _barVisuals.Clear();

        var count = Math.Clamp(BarCount, 3, 12);
        var height = Math.Max(10, MaxBarHeight);
        var accentBrush = Application.Current.Resources["NovaAccentBrush"] as Brush;

        for (var i = 0; i < count; i++)
        {
            var bar = new Rectangle
            {
                Width = 2.5,
                Height = height,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = accentBrush
            };

            BarsHost.Children.Add(bar);

            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3(1.25f, (float)height, 0f);
            visual.Scale = new Vector3(1f, 0.22f, 1f);
            _barVisuals.Add(visual);
        }

        UpdateBars();
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands)
    {
        var arr = new double[bands.Length];
        for (var i = 0; i < bands.Length; i++)
        {
            arr[i] = bands[i];
        }
        Levels = arr;
    }

    private void UpdateBars()
    {
        if (_barVisuals.Count == 0)
        {
            return;
        }

        var levels = Levels;
        for (var i = 0; i < _barVisuals.Count; i++)
        {
            var visual = _barVisuals[i];
            float targetScale;
            if (!IsActive)
            {
                targetScale = 0.22f;
            }
            else if (levels is not null && levels.Length > 0)
            {
                var idx = i * levels.Length / _barVisuals.Count;
                targetScale = (float)Math.Clamp(levels[idx], 0.18, 1.0);
            }
            else
            {
                targetScale = 0.45f;
            }

            var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, targetScale);
            anim.Duration = TimeSpan.FromMilliseconds(95);
            visual.StartAnimation("Scale.Y", anim);
        }
    }
}
