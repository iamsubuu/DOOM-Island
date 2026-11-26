using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Services;
using NOVAIsland.Core.ViewModels;

namespace NOVAIsland.App.Views.Island;

public sealed partial class TimerPanelView : UserControl
{
    private TimerViewModel? _vm;

    public TimerPanelView()
    {
        InitializeComponent();
    }

    public void Bind(TimerViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        if (_vm is null) return;

        Ring.Progress = _vm.ProgressFraction;
        TimeDisplayText.Text = _vm.DisplayTime;
        PhaseDisplayText.Text = _vm.PhaseOrModeLabel;
        StatusText.Text = _vm.StatusLabel;
        StartPauseBtn.Content = _vm.IsActiveTabRunning ? "Pause" : "Start";

        PomoPresets.Visibility = _vm.ActiveTab == TimerTab.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        CountdownPresets.Visibility = _vm.ActiveTab == TimerTab.Countdown ? Visibility.Visible : Visibility.Collapsed;
        LapsText.Visibility = _vm.ActiveTab == TimerTab.Stopwatch ? Visibility.Visible : Visibility.Collapsed;
        LapBtn.Visibility = _vm.ActiveTab == TimerTab.Stopwatch ? Visibility.Visible : Visibility.Collapsed;
        LapBtn.IsEnabled = _vm.IsStopwatchRunning;

        if (_vm.ActiveTab == TimerTab.Stopwatch)
        {
            LapsText.Text = _vm.StopwatchLaps.Count == 0
                ? "Press Lap while running to record splits."
                : string.Join("   ", _vm.StopwatchLaps.Select((l, i) => $"L{_vm.StopwatchLaps.Count - i}: {TimeFormatter.FormatStopwatch(l)}"));
        }
    }

    private void OnPomodoroTabClick(object sender, RoutedEventArgs e) => _vm?.SelectTab(TimerTab.Pomodoro);
    private void OnCountdownTabClick(object sender, RoutedEventArgs e) => _vm?.SelectTab(TimerTab.Countdown);
    private void OnStopwatchTabClick(object sender, RoutedEventArgs e) => _vm?.SelectTab(TimerTab.Stopwatch);
    private void OnStartPauseClick(object sender, RoutedEventArgs e) => _vm?.ToggleActiveTimer();
    private void OnResetClick(object sender, RoutedEventArgs e) => _vm?.ResetActiveTimer();
    private void OnLapClick(object sender, RoutedEventArgs e) => _vm?.LapStopwatch();

    private void OnPomoPresetClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null && sender is Button b && b.Tag is string tag && Enum.TryParse<PomodoroPhase>(tag, out var phase))
        {
            _vm.SetPomodoroPhase(phase);
        }
    }

    private void OnCountdownPresetClick(object sender, RoutedEventArgs e)
    {
        if (_vm is not null && sender is Button b && b.Tag is string tag && int.TryParse(tag, out var secs))
        {
            _vm.SetCountdownPreset(secs);
        }
    }
}
