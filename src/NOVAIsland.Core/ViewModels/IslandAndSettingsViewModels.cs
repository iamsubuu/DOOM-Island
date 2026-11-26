using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Core.ViewModels;

public sealed partial class IslandViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    private IslandMode _mode = IslandMode.Compact;

    [ObservableProperty]
    private bool _isHovered;

    [ObservableProperty]
    private int _notificationPulseKey;

    [ObservableProperty]
    private string? _toastMessage;

    public MusicViewModel Music { get; }
    public LyricsViewModel Lyrics { get; }
    public ClipboardViewModel Clipboard { get; }
    public NotificationsViewModel Notifications { get; }
    public TimerViewModel Timer { get; }
    public SystemViewModel System { get; }
    public ActivityViewModel Activity { get; }

    public bool IsExpanded => Mode is not IslandMode.Compact and not IslandMode.Music;

    public IslandViewModel(
        MusicViewModel music,
        LyricsViewModel lyrics,
        ClipboardViewModel clipboard,
        NotificationsViewModel notifications,
        TimerViewModel timer,
        SystemViewModel system,
        ActivityViewModel activity)
    {
        Music = music;
        Lyrics = lyrics;
        Clipboard = clipboard;
        Notifications = notifications;
        Timer = timer;
        System = system;
        Activity = activity;
    }

    [RelayCommand]
    public void SetMode(IslandMode mode) => Mode = mode;

    [RelayCommand]
    public void Collapse(bool hasActiveMedia) => Collapse(hasActiveMedia, stealthMode: false);

    public void Collapse(bool hasActiveMedia, bool stealthMode)
    {
        Mode = !stealthMode && hasActiveMedia ? IslandMode.Music : IslandMode.Compact;
    }

    [RelayCommand]
    public void ToggleExpand(bool hasActiveMedia) => ToggleExpand(hasActiveMedia, stealthMode: false);

    public void ToggleExpand(bool hasActiveMedia, bool stealthMode)
    {
        if (IsExpanded)
        {
            Collapse(hasActiveMedia, stealthMode);
        }
        else
        {
            Mode = hasActiveMedia ? IslandMode.Expanded : IslandMode.System;
        }
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty]
    private AppSettings _settings = new();

    [ObservableProperty]
    private SettingsSectionId _activeSection = SettingsSectionId.General;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public string ResolvedAppName =>
        string.IsNullOrWhiteSpace(Settings.General.AppName)
            ? AppConstants.DefaultAppName
            : Settings.General.AppName.Trim();

    public string ResolvedShortName =>
        string.IsNullOrWhiteSpace(Settings.General.AppShortName)
            ? AppConstants.DefaultShortName
            : Settings.General.AppShortName.Trim();

    public event EventHandler? SettingsChanged;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Settings = await _store.LoadAsync(cancellationToken);
        OnPropertyChanged(nameof(ResolvedAppName));
        OnPropertyChanged(nameof(ResolvedShortName));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _store.SaveAsync(Settings, cancellationToken);
        OnPropertyChanged(nameof(ResolvedAppName));
        OnPropertyChanged(nameof(ResolvedShortName));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task ResetAppearanceAsync()
    {
        Settings.Appearance = new AppearanceSettings();
        await SaveAsync();
    }

    [RelayCommand]
    public async Task ResetPositionAsync()
    {
        Settings.Position = new PositionSettings();
        await SaveAsync();
    }

    [RelayCommand]
    public async Task ResetShortcutsAsync()
    {
        Settings.Shortcuts = AppSettings.CreateDefaultShortcuts();
        await SaveAsync();
    }

    [RelayCommand]
    public async Task ResetAllAsync()
    {
        await _store.ResetAsync();
        await LoadAsync();
    }
}
