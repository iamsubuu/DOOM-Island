using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.ViewModels;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class NotificationsPanelView : UserControl
{
    private NotificationsViewModel? _vm;
    private SettingsViewModel? _settings;

    public NotificationsPanelView()
    {
        InitializeComponent();
    }

    public void Bind(NotificationsViewModel vm, SettingsViewModel settings)
    {
        _vm = vm;
        _settings = settings;
        _vm.VisibleNotifications.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(Rebuild);
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Rebuild);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_vm is null) return;

        ActiveTabBtn.Content = $"Active ({_vm.ActiveCount})";
        PermissionBanner.Visibility = _vm.HasPermission ? Visibility.Collapsed : Visibility.Visible;
        PermissionStatusText.Text = _vm.StatusMessage;

        ListHost.Children.Clear();
        if (_vm.VisibleNotifications.Count == 0)
        {
            ListHost.Children.Add(new TextBlock
            {
                Text = _vm.HasPermission ? "All caught up. No notifications." : "Waiting for notification access.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 113, 113, 122)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            });
            return;
        }

        var showPreviews = _settings?.Settings.Notifications.ShowPreviews ?? true;
        foreach (var item in _vm.VisibleNotifications)
        {
            var card = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
                Opacity = item.Dismissed ? 0.55 : 1.0
            };

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = $"{item.AppName} · {item.TimestampUtc.ToLocalTime():t}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170))
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = showPreviews ? item.Body : "Message preview hidden by notification privacy setting.",
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170))
            });

            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            if (!item.Dismissed)
            {
                var dismiss = new Button { Content = "Dismiss", FontSize = 11, Padding = new Thickness(8, 4, 8, 4) };
                var captured = item;
                dismiss.Click += async (_, _) => await _vm.DismissAsync(captured);
                Grid.SetColumn(dismiss, 1);
                grid.Children.Add(dismiss);
            }

            card.Child = grid;
            ListHost.Children.Add(card);
        }
    }

    private void OnActiveTabClick(object sender, RoutedEventArgs e) => _vm?.SetShowHistory(false);
    private void OnHistoryTabClick(object sender, RoutedEventArgs e) => _vm?.SetShowHistory(true);
    private void OnMarkAllReadClick(object sender, RoutedEventArgs e) => _vm?.MarkAllRead();
    private void OnClearClick(object sender, RoutedEventArgs e) => _vm?.ClearAll();
}
