using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.ViewModels;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class SystemPanelView : UserControl
{
    private SystemViewModel? _vm;
    private bool _updatingUi;

    public SystemPanelView()
    {
        InitializeComponent();
    }

    public void Bind(SystemViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        if (_vm is null) return;
        _updatingUi = true;
        try
        {
            var m = _vm.Metrics;
            WifiValueText.Text = m.WifiConnected ? m.WifiNetwork : "Disconnected";
            BatteryPctText.Text = m.HasBattery ? $"{m.BatteryLevel}%" : "AC Power";
            BatteryStateText.Text = !m.HasBattery ? "Desktop" : m.IsCharging ? "Charging" : "On Battery";
            MicStateText.Text = m.MicMuted ? "Muted" : m.MicActive ? "In Use" : "Ready";
            CamStateText.Text = m.CameraActive ? "In Use" : "Standby";

            VolumeSlider.Value = m.IsMuted ? 0 : m.Volume;
            VolumePctText.Text = $"{(m.IsMuted ? 0 : m.Volume)}%";

            BrightnessSlider.IsEnabled = m.BrightnessSupported;
            BrightnessSlider.Value = m.Brightness;
            BrightnessPctText.Text = m.BrightnessSupported ? $"{m.Brightness}%" : "N/A";

            CpuPctText.Text = $"{m.CpuUsage}%";
            CpuBar.Value = m.CpuUsage;
            RamText.Text = $"{m.RamUsedGb:0.1} / {m.RamTotalGb:0.#} GB";
            RamBar.Value = m.RamUsage;

            AudioDevicesGrid.Items.Clear();
            foreach (var dev in m.AudioDevices)
            {
                var selected = dev.Id == m.ActiveAudioDeviceId;
                var btn = new Button
                {
                    Content = dev.Name,
                    Width = 240,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    FontSize = 11.5,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(selected ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(8, 255, 255, 255)),
                    Foreground = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(255, 161, 161, 170)),
                    BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(15, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };
                var id = dev.Id;
                btn.Click += async (_, _) =>
                {
                    if (_vm is not null)
                    {
                        await _vm.SelectAudioDeviceAsync(id);
                        await _vm.RefreshAsync();
                    }
                };
                AudioDevicesGrid.Items.Add(btn);
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private async void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi || _vm is null) return;
        var val = (int)Math.Round(e.NewValue);
        VolumePctText.Text = $"{val}%";
        await _vm.SetVolumeAsync(val, val == 0);
    }

    private async void OnBrightnessChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi || _vm is null || !BrightnessSlider.IsEnabled) return;
        var val = (int)Math.Round(e.NewValue);
        BrightnessPctText.Text = $"{val}%";
        await _vm.SetBrightnessAsync(val);
    }

    private async void OnToggleMuteClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            await _vm.ToggleMuteAsync();
            await _vm.RefreshAsync();
        }
    }

    private async void OnToggleMicClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            await _vm.ToggleMicMuteAsync();
            await _vm.RefreshAsync();
        }
    }
}
