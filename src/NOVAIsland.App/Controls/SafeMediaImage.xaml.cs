using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NOVAIsland.App.Controls;

public sealed partial class SafeMediaImage : UserControl
{
    public static readonly DependencyProperty SourceUriProperty = DependencyProperty.Register(
        nameof(SourceUri),
        typeof(string),
        typeof(SafeMediaImage),
        new PropertyMetadata(null, OnSourceUriChanged));

    public static readonly DependencyProperty ImageCornerRadiusProperty = DependencyProperty.Register(
        nameof(ImageCornerRadius),
        typeof(CornerRadius),
        typeof(SafeMediaImage),
        new PropertyMetadata(new CornerRadius(10)));

    public string? SourceUri
    {
        get => (string?)GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    public CornerRadius ImageCornerRadius
    {
        get => (CornerRadius)GetValue(ImageCornerRadiusProperty);
        set => SetValue(ImageCornerRadiusProperty, value);
    }

    public SafeMediaImage()
    {
        InitializeComponent();
    }

    private static void OnSourceUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SafeMediaImage control)
        {
            control.UpdateImage(e.NewValue as string);
        }
    }

    private void UpdateImage(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            MediaImage.Source = null;
            MediaImage.Visibility = Visibility.Collapsed;
            FallbackBorder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            MediaImage.Source = new BitmapImage(uri);
            MediaImage.Visibility = Visibility.Visible;
            FallbackBorder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            MediaImage.Visibility = Visibility.Collapsed;
            FallbackBorder.Visibility = Visibility.Visible;
        }
    }

    private void OnImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MediaImage.Visibility = Visibility.Collapsed;
        FallbackBorder.Visibility = Visibility.Visible;
    }

    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        MediaImage.Visibility = Visibility.Visible;
        FallbackBorder.Visibility = Visibility.Collapsed;
    }
}
