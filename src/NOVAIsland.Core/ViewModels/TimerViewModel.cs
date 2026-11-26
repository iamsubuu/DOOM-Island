using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Services;

namespace NOVAIsland.Core.ViewModels;

public sealed partial class TimerViewModel : ObservableObject
{
    public event EventHandler<string>? TimerCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    [NotifyPropertyChangedFor(nameof(PhaseOrModeLabel))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsActiveTabRunning))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private TimerTab _activeTab = TimerTab.Pomodoro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private int _countdownDurationSeconds = 600;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyPropertyChangedFor(nameof(CompactBadgeText))]
    private int _countdownRemainingSeconds = 600;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveTabRunning))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(CompactBadgeText))]
    private bool _isCountdownRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private long _stopwatchTenths;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveTabRunning))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _isStopwatchRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhaseOrModeLabel))]
    private PomodoroPhase _pomodoroPhase = PomodoroPhase.Focus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private int _pomodoroDurationSeconds = 25 * 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyPropertyChangedFor(nameof(CompactBadgeText))]
    private int _pomodoroRemainingSeconds = 25 * 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveTabRunning))]
    [NotifyPropertyChangedFor(nameof(CompactBadgeText))]
    private bool _isPomodoroRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private int _pomodoroSessionsCompleted;

    public IReadOnlyList<long> StopwatchLaps => _stopwatchLaps;
    private readonly List<long> _stopwatchLaps = [];

    public bool IsActiveTabRunning => ActiveTab switch
    {
        TimerTab.Countdown => IsCountdownRunning,
        TimerTab.Stopwatch => IsStopwatchRunning,
        _ => IsPomodoroRunning
    };

    public string DisplayTime => ActiveTab switch
    {
        TimerTab.Countdown => TimeFormatter.FormatTime(CountdownRemainingSeconds),
        TimerTab.Stopwatch => TimeFormatter.FormatStopwatch(StopwatchTenths),
        _ => TimeFormatter.FormatTime(PomodoroRemainingSeconds)
    };

    public string PhaseOrModeLabel => ActiveTab switch
    {
        TimerTab.Countdown => "Remaining",
        TimerTab.Stopwatch => "Elapsed",
        _ => PomodoroPhase switch
        {
            PomodoroPhase.Focus => "Focus",
            PomodoroPhase.ShortBreak => "Short Break",
            _ => "Long Break"
        }
    };

    public string StatusLabel => ActiveTab switch
    {
        TimerTab.Pomodoro => $"Session {PomodoroSessionsCompleted + 1}",
        TimerTab.Stopwatch => $"{_stopwatchLaps.Count} Laps",
        _ => IsCountdownRunning ? "Active" : "Ready"
    };

    public double ProgressFraction => ActiveTab switch
    {
        TimerTab.Countdown => CountdownDurationSeconds > 0
            ? Math.Clamp((double)CountdownRemainingSeconds / CountdownDurationSeconds, 0, 1)
            : 0,
        TimerTab.Stopwatch => (StopwatchTenths % 600) / 600.0,
        _ => PomodoroDurationSeconds > 0
            ? Math.Clamp((double)PomodoroRemainingSeconds / PomodoroDurationSeconds, 0, 1)
            : 0
    };

    public string? CompactBadgeText
    {
        get
        {
            if (IsPomodoroRunning)
            {
                return TimeFormatter.FormatTime(PomodoroRemainingSeconds);
            }

            if (IsCountdownRunning)
            {
                return TimeFormatter.FormatTime(CountdownRemainingSeconds);
            }

            return null;
        }
    }

    [RelayCommand]
    public void SelectTab(TimerTab tab) => ActiveTab = tab;

    [RelayCommand]
    public void ToggleActiveTimer()
    {
        switch (ActiveTab)
        {
            case TimerTab.Countdown:
                if (CountdownRemainingSeconds > 0)
                {
                    IsCountdownRunning = !IsCountdownRunning;
                }
                break;
            case TimerTab.Stopwatch:
                IsStopwatchRunning = !IsStopwatchRunning;
                break;
            case TimerTab.Pomodoro:
                IsPomodoroRunning = !IsPomodoroRunning;
                break;
        }
    }

    [RelayCommand]
    public void ResetActiveTimer()
    {
        switch (ActiveTab)
        {
            case TimerTab.Countdown:
                IsCountdownRunning = false;
                CountdownRemainingSeconds = CountdownDurationSeconds;
                break;
            case TimerTab.Stopwatch:
                IsStopwatchRunning = false;
                StopwatchTenths = 0;
                _stopwatchLaps.Clear();
                OnPropertyChanged(nameof(StopwatchLaps));
                OnPropertyChanged(nameof(StatusLabel));
                break;
            case TimerTab.Pomodoro:
                IsPomodoroRunning = false;
                PomodoroRemainingSeconds = PomodoroDurationSeconds;
                break;
        }
    }

    [RelayCommand]
    public void SetCountdownPreset(int seconds)
    {
        var safe = Math.Max(1, seconds);
        IsCountdownRunning = false;
        CountdownDurationSeconds = safe;
        CountdownRemainingSeconds = safe;
    }

    [RelayCommand]
    public void SetPomodoroPhase(PomodoroPhase phase)
    {
        var duration = phase switch
        {
            PomodoroPhase.Focus => 25 * 60,
            PomodoroPhase.ShortBreak => 5 * 60,
            _ => 15 * 60
        };

        IsPomodoroRunning = false;
        PomodoroPhase = phase;
        PomodoroDurationSeconds = duration;
        PomodoroRemainingSeconds = duration;
    }

    [RelayCommand]
    public void LapStopwatch()
    {
        if (!IsStopwatchRunning)
        {
            return;
        }

        _stopwatchLaps.Insert(0, StopwatchTenths);
        if (_stopwatchLaps.Count > 6)
        {
            _stopwatchLaps.RemoveAt(_stopwatchLaps.Count - 1);
        }

        OnPropertyChanged(nameof(StopwatchLaps));
        OnPropertyChanged(nameof(StatusLabel));
    }

    public void TickOneSecond()
    {
        if (IsCountdownRunning)
        {
            if (CountdownRemainingSeconds <= 1)
            {
                CountdownRemainingSeconds = 0;
                IsCountdownRunning = false;
                TimerCompleted?.Invoke(this, "Countdown finished");
            }
            else
            {
                CountdownRemainingSeconds--;
            }
        }

        if (IsPomodoroRunning)
        {
            if (PomodoroRemainingSeconds <= 1)
            {
                var finishedPhase = PomodoroPhase;
                if (finishedPhase == PomodoroPhase.Focus)
                {
                    PomodoroSessionsCompleted++;
                }

                var nextPhase = finishedPhase == PomodoroPhase.Focus
                    ? PomodoroPhase.ShortBreak
                    : PomodoroPhase.Focus;

                SetPomodoroPhase(nextPhase);
                TimerCompleted?.Invoke(
                    this,
                    finishedPhase == PomodoroPhase.Focus
                        ? "Focus session complete — ready for break"
                        : "Break complete — ready to focus");
            }
            else
            {
                PomodoroRemainingSeconds--;
            }
        }
    }

    public void TickStopwatchTenth()
    {
        if (IsStopwatchRunning)
        {
            StopwatchTenths++;
        }
    }
}
