using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Core.ViewModels;

public sealed partial class NotificationsViewModel : ObservableObject
{
    private readonly INotificationListenerService _listener;
    private readonly List<NotificationItem> _items = [];

    public ObservableCollection<NotificationItem> VisibleNotifications { get; } = [];

    [ObservableProperty]
    private bool _showHistory;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasPermission;

    public NotificationsViewModel(INotificationListenerService listener)
    {
        _listener = listener;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _listener.InitializeAsync(cancellationToken);
        HasPermission = _listener.HasPermission;
        StatusMessage = _listener.StatusMessage;

        if (HasPermission)
        {
            var existing = await _listener.GetActiveAsync(cancellationToken);
            _items.Clear();
            _items.AddRange(existing);
            RebuildVisible();
        }
    }

    public void AddOrUpdate(NotificationItem item)
    {
        _items.RemoveAll(n => n.Id == item.Id);
        _items.Insert(0, item);
        RebuildVisible();
    }

    [RelayCommand]
    public void SetShowHistory(bool showHistory)
    {
        ShowHistory = showHistory;
        RebuildVisible();
    }

    [RelayCommand]
    public async Task DismissAsync(NotificationItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _listener.DismissAsync(item);
        var idx = _items.FindIndex(n => n.Id == item.Id);
        if (idx >= 0)
        {
            _items[idx] = _items[idx] with { Dismissed = true, Read = true };
        }
        RebuildVisible();
    }

    [RelayCommand]
    public void MarkAllRead()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i] = _items[i] with { Read = true };
        }
        RebuildVisible();
    }

    [RelayCommand]
    public void ClearAll()
    {
        _items.Clear();
        RebuildVisible();
    }

    private void RebuildVisible()
    {
        VisibleNotifications.Clear();
        foreach (var item in _items.Where(n => ShowHistory || !n.Dismissed))
        {
            VisibleNotifications.Add(item);
        }

        ActiveCount = _items.Count(n => !n.Dismissed);
        UnreadCount = _items.Count(n => !n.Read && !n.Dismissed);
    }
}

public sealed partial class SystemViewModel : ObservableObject
{
    private readonly ISystemMonitorService _systemService;

    [ObservableProperty]
    private SystemMetrics _metrics = new(
        BatteryLevel: 100,
        HasBattery: false,
        IsCharging: true,
        Volume: 50,
        IsMuted: false,
        Brightness: 100,
        BrightnessSupported: false,
        WifiConnected: false,
        WifiNetwork: "Disconnected",
        WifiSignal: 0,
        MicActive: false,
        MicMuted: false,
        CameraActive: false,
        CpuUsage: 0,
        RamUsage: 0,
        RamUsedGb: 0,
        RamTotalGb: 0,
        ActiveAudioDeviceId: null,
        AudioDevices: []);

    public SystemViewModel(ISystemMonitorService systemService)
    {
        _systemService = systemService;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Metrics = await _systemService.CaptureMetricsAsync(cancellationToken);
    }

    public Task SetVolumeAsync(int volumePercent, bool muted) =>
        _systemService.SetMasterVolumeAsync(Math.Clamp(volumePercent, 0, 100), muted);

    [RelayCommand]
    public Task ToggleMuteAsync() =>
        _systemService.SetMasterVolumeAsync(Metrics.Volume, !Metrics.IsMuted);

    [RelayCommand]
    public Task ToggleMicMuteAsync() =>
        _systemService.SetMicrophoneMutedAsync(!Metrics.MicMuted);

    public Task<bool> SetBrightnessAsync(int brightnessPercent) =>
        _systemService.SetBrightnessAsync(Math.Clamp(brightnessPercent, 0, 100));

    [RelayCommand]
    public Task<bool> SelectAudioDeviceAsync(string? deviceId) =>
        string.IsNullOrEmpty(deviceId) ? Task.FromResult(false) : _systemService.SetDefaultAudioDeviceAsync(deviceId);
}

public sealed partial class ActivityViewModel : ObservableObject
{
    private readonly IActivityMonitorService _activityService;

    public ObservableCollection<ActivityItem> Items { get; } = [];

    [ObservableProperty]
    private int _activeCount;

    public ActivityViewModel(IActivityMonitorService activityService)
    {
        _activityService = activityService;
        _activityService.ActivitiesChanged += (_, updated) => Apply(updated);
    }

    public void Apply(IReadOnlyList<ActivityItem> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
        ActiveCount = items.Count(a => a.Status == ActivityStatus.Active);
    }

    [RelayCommand]
    public Task CaptureScreenshotAsync() => _activityService.CaptureQuickScreenshotAsync();

    [RelayCommand]
    public Task SaveScreenshotAsync(ActivityItem? item) =>
        item is null ? Task.FromResult(false) : _activityService.SaveScreenshotAsAsync(item);
}
