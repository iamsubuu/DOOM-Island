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

public sealed partial class ClipboardPanelView : UserControl
{
    private ClipboardViewModel? _vm;

    public ClipboardPanelView()
    {
        InitializeComponent();
    }

    public void Bind(ClipboardViewModel vm)
    {
        _vm = vm;
        _vm.FilteredItems.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(RebuildItems);
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RebuildItems);
        RebuildItems();
    }

    private void RebuildItems()
    {
        if (_vm is null) return;
        ItemsHost.Children.Clear();

        if (_vm.FilteredItems.Count == 0)
        {
            ItemsHost.Children.Add(new TextBlock
            {
                Text = "No clipboard items match your filter.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 113, 113, 122)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 24)
            });
            return;
        }

        foreach (var item in _vm.FilteredItems)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 8, 8, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
            };

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Spacing = 4 };
            left.Children.Add(new TextBlock
            {
                Text = $"{item.PreviewTitle ?? item.Type.ToString()} · {item.SourceApp}",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 228, 228, 231)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (item.Type == ClipboardItemType.Image && !string.IsNullOrWhiteSpace(item.ImagePath))
            {
                var imgRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                imgRow.Children.Add(new SafeMediaImage
                {
                    Width = 76,
                    Height = 46,
                    ImageCornerRadius = new CornerRadius(6),
                    SourceUri = item.ImagePath
                });
                imgRow.Children.Add(new TextBlock
                {
                    Text = item.Content,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170))
                });
                left.Children.Add(imgRow);
            }
            else
            {
                left.Children.Add(new TextBlock
                {
                    Text = item.Content,
                    FontSize = item.Type == ClipboardItemType.Code ? 11 : 12,
                    MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush(item.Type == ClipboardItemType.Link
                        ? Color.FromArgb(255, 125, 211, 252)
                        : Color.FromArgb(255, 212, 212, 216))
                });
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
            var copyBtn = new Button { Content = _vm.RecentlyCopiedId == item.Id ? "Copied" : "Copy", FontSize = 11, Padding = new Thickness(8, 4, 8, 4) };
            var pinBtn = new Button { Content = item.Pinned ? "Unpin" : "Pin", FontSize = 11, Padding = new Thickness(8, 4, 8, 4) };
            var delBtn = new Button { Content = "Del", FontSize = 11, Padding = new Thickness(8, 4, 8, 4) };

            var captured = item;
            copyBtn.Click += async (_, _) => await _vm.CopyItemAsync(captured);
            pinBtn.Click += async (_, _) => await _vm.TogglePinAsync(captured.Id);
            delBtn.Click += async (_, _) => await _vm.DeleteItemAsync(captured.Id);

            actions.Children.Add(copyBtn);
            actions.Children.Add(pinBtn);
            actions.Children.Add(delBtn);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(left);
            grid.Children.Add(actions);
            row.Child = grid;
            ItemsHost.Children.Add(row);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm is not null) _vm.SearchQuery = SearchBox.Text;
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button btn || btn.Tag is not string tag) return;
        if (Enum.TryParse<ClipboardFilterKind>(tag, out var filter))
        {
            _vm.SetFilter(filter);
        }
    }

    private async void OnClearUnpinnedClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.ClearUnpinnedAsync();
    }
}
