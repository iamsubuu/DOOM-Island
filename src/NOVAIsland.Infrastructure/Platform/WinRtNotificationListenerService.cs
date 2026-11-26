using Microsoft.Win32;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Wraps WinRT UserNotificationListener with honest Package Identity & consent verification.
/// </summary>
public sealed class WinRtNotificationListenerService : INotificationListenerService
{
    private UserNotificationListener? _listener;

    public bool IsSupported { get; private set; }
    public bool HasPermission { get; private set; }
    public string StatusMessage { get; private set; } = "Not initialized";

    public event EventHandler<NotificationItem>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync().AsTask(cancellationToken);

            switch (status)
            {
                case UserNotificationListenerAccessStatus.Allowed:
                    IsSupported = true;
                    HasPermission = true;
                    StatusMessage = "Connected to Windows Action Center";
                    _listener.NotificationChanged += OnNotificationChanged;
                    break;
                case UserNotificationListenerAccessStatus.Denied:
                    IsSupported = true;
                    HasPermission = false;
                    StatusMessage = "Notification access denied in Windows Privacy Settings";
                    break;
                default:
                    IsSupported = false;
                    HasPermission = false;
                    StatusMessage = "Requires MSIX / Sparse Package Identity for Windows Notification Listener";
                    break;
            }
        }
        catch (Exception ex)
        {
            IsSupported = false;
            HasPermission = false;
            StatusMessage = $"Notification listener unavailable in unpackaged mode ({ex.GetType().Name})";
        }
    }

    public async Task<IReadOnlyList<NotificationItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPermission || _listener is null)
        {
            return [];
        }

        var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken);
        return raw.Select(MapNotification).OrderByDescending(n => n.TimestampUtc).ToList();
    }

    public Task DismissAsync(NotificationItem item, CancellationToken cancellationToken = default)
    {
        if (HasPermission && _listener is not null && item.PlatformNotificationId is uint nativeId)
        {
            try
            {
                _listener.RemoveNotification(nativeId);
            }
            catch
            {
                // Notification may already have been cleared by user
            }
        }

        return Task.CompletedTask;
    }

    public bool IsFocusAssistActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", false);
            var enabled = key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
            return enabled is int val && val == 0;
        }
        catch
        {
            return false;
        }
    }

    private async void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind == UserNotificationChangedKind.Removed)
        {
            NotificationRemoved?.Invoke(this, args.UserNotificationId);
            return;
        }

        try
        {
            var all = await sender.GetNotificationsAsync(NotificationKinds.Toast);
            var match = all.FirstOrDefault(n => n.Id == args.UserNotificationId);
            if (match is not null)
            {
                NotificationReceived?.Invoke(this, MapNotification(match));
            }
        }
        catch
        {
            // Ignore transient WinRT read race
        }
    }

    private static NotificationItem MapNotification(UserNotification n)
    {
        var appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "Windows System";
        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];

        var title = texts.Count > 0 ? texts[0] : appName;
        var body = texts.Count > 1 ? string.Join(" — ", texts.Skip(1)) : string.Empty;

        return new NotificationItem(
            Id: $"winrt-{n.Id}",
            AppName: appName,
            Title: title,
            Body: body,
            TimestampUtc: n.CreationTime,
            Read: false,
            Dismissed: false,
            ActionLabel: null,
            PlatformNotificationId: n.Id);
    }
}
