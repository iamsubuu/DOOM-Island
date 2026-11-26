using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.App.Controls;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.ViewModels;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class ActivityPanelView : UserControl
{
    private ActivityViewModel? _vm;

    public ActivityPanelView()
    {
        InitializeComponent();
    }

    public void Bind(ActivityViewModel vm)
    {
        _vm = vm;
        _vm.Items.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(Rebuild);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_vm is null) return;
        FeedHost.Children.Clear();

        if (_vm.Items.Count == 0)
        {
            FeedHost.Children.Add(new TextBlock
            {
                Text = "No active downloads or captures detected yet.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 113, 113, 122)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 22, 0, 22)
            });
            return;
        }

        foreach (var act in _vm.Items)
        {
            var card = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
            };

            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new TextBlock
            {
                Text = act.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{act.Subtitle} · {act.SizeText}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (act.Type == ActivityType.Screenshot && !string.IsNullOrWhiteSpace(act.FilePath))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(new SafeMediaImage
                {
                    Width = 96,
                    Height = 56,
                    ImageCornerRadius = new CornerRadius(6),
                    SourceUri = act.FilePath
                });
                var saveBtn = new Button { Content = "Save PNG", FontSize = 11, Padding = new Thickness(10, 4, 10, 4) };
                var captured = act;
                saveBtn.Click += async (_, _) => await _vm.SaveScreenshotAsync(captured);
                row.Children.Add(saveBtn);
                stack.Children.Add(row);
            }
            else
            {
                stack.Children.Add(new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = act.Progress,
                    Height = 4
                });
            }

            card.Child = stack;
            FeedHost.Children.Add(card);
        }
    }

    private async void OnSnipClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.CaptureScreenshotAsync();
    }
}
