using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Reads Windows Action Center system notifications via WinRT UserNotificationListener when packaged (MSIX),
/// and directly via Windows' system Action Center SQLite store (wpndatabase.db) when running unpackaged (EXE).
/// </summary>
public sealed class WinRtNotificationListenerService : INotificationListenerService, IDisposable
{
    private UserNotificationListener? _listener;
    private FileSystemWatcher? _wpnWatcher;
    private CancellationTokenSource? _pollCts;
    private readonly string _wpnDbPath;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, NotificationItem> _knownSystemToasts = new();
    private readonly ConcurrentDictionary<uint, byte> _locallyDismissedIds = new();
    private DateTime _lastDbStampUtc = DateTime.MinValue;
    private long _lastDbSignature;
    private bool _usingSystemDatabase;

    public bool IsSupported { get; private set; }
    public bool HasPermission { get; private set; }
    public string StatusMessage { get; private set; } = "Initializing Windows System Notifications...";

    public event EventHandler<NotificationItem>? NotificationReceived;
    public event EventHandler<uint>? NotificationRemoved;

    public WinRtNotificationListenerService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Notifications", "wpndatabase.db"))
    {
    }

    internal WinRtNotificationListenerService(string wpnDbPath)
    {
        _wpnDbPath = wpnDbPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (HasPackageIdentity())
        {
            try
            {
                _listener = UserNotificationListener.Current;
                var status = await _listener.RequestAccessAsync().AsTask(cancellationToken);

                if (status == UserNotificationListenerAccessStatus.Allowed)
                {
                    _usingSystemDatabase = false;
                    IsSupported = true;
                    HasPermission = true;
                    StatusMessage = "Connected to Windows System Action Center (Live API)";
                    _listener.NotificationChanged += OnNotificationChanged;
                    return;
                }
            }
            catch
            {
                _listener = null;
            }
        }

        // Unpackaged EXE mode (or denied WinRT fallback): read Windows system wpndatabase.db directly.
        _usingSystemDatabase = true;
        if (File.Exists(_wpnDbPath))
        {
            IsSupported = true;
            HasPermission = true;
            StatusMessage = "Connected to Windows System Notifications";
            await RefreshFromSystemDatabaseAsync(raiseEvents: false, cancellationToken);
            StartSystemDatabaseMonitor();
        }
        else
        {
            IsSupported = true;
            HasPermission = true;
            StatusMessage = "Windows System Notifications ready (waiting for first system alert)";
            StartSystemDatabaseMonitor();
        }
    }

    public async Task<IReadOnlyList<NotificationItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPermission)
        {
            return [];
        }

        if (!_usingSystemDatabase && _listener is not null)
        {
            try
            {
                var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken);
                return raw.Select(MapNotification).OrderByDescending(n => n.TimestampUtc).ToList();
            }
            catch
            {
                return [];
            }
        }

        await RefreshFromSystemDatabaseAsync(raiseEvents: false, cancellationToken);
        return _knownSystemToasts.Values
            .OrderByDescending(n => n.TimestampUtc)
            .ToList();
    }

    public Task DismissAsync(NotificationItem item, CancellationToken cancellationToken = default)
    {
        if (item.PlatformNotificationId is not uint nativeId)
        {
            return Task.CompletedTask;
        }

        _locallyDismissedIds[nativeId] = 1;
        _knownSystemToasts.TryRemove(nativeId, out _);

        if (!_usingSystemDatabase && HasPermission && _listener is not null)
        {
            try
            {
                _listener.RemoveNotification(nativeId);
            }
            catch
            {
                // Notification may already have been cleared by user.
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

    private void StartSystemDatabaseMonitor()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        try
        {
            var dir = Path.GetDirectoryName(_wpnDbPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                _wpnWatcher?.Dispose();
                _wpnWatcher = new FileSystemWatcher(dir, "wpndatabase.db*")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _wpnWatcher.Changed += (_, _) => _ = RefreshSafeAsync(ct);
                _wpnWatcher.Created += (_, _) => _ = RefreshSafeAsync(ct);
            }
        }
        catch
        {
            // Polling timer below still refreshes system notifications reliably.
        }

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(ct))
                    {
                        break;
                    }

                    await RefreshFromSystemDatabaseAsync(raiseEvents: true, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Ignore transient SQLite read contention during Windows toast writes.
                }
            }
        }, ct);
    }

    private async Task RefreshSafeAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(120, ct);
            await RefreshFromSystemDatabaseAsync(raiseEvents: true, ct);
        }
        catch
        {
            // Ignore transient watcher triggers.
        }
    }

    private async Task RefreshFromSystemDatabaseAsync(bool raiseEvents, CancellationToken cancellationToken)
    {
        if (!File.Exists(_wpnDbPath))
        {
            return;
        }

        try
        {
            var dbInfo = new FileInfo(_wpnDbPath);
            var walInfo = new FileInfo(_wpnDbPath + "-wal");
            var stamp = dbInfo.LastWriteTimeUtc;
            long sig = dbInfo.Length;
            if (walInfo.Exists)
            {
                if (walInfo.LastWriteTimeUtc > stamp) stamp = walInfo.LastWriteTimeUtc;
                sig ^= walInfo.Length << 17;
            }

            if (raiseEvents && stamp == _lastDbStampUtc && sig == _lastDbSignature)
            {
                return;
            }

            _lastDbStampUtc = stamp;
            _lastDbSignature = sig;
        }
        catch
        {
            // Continue into locked SQLite check if file stat fails transiently.
        }

        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(() => ReadSystemToastsFromSqlite(_wpnDbPath), cancellationToken);
            var activeIds = new HashSet<uint>();

            foreach (var item in snapshot)
            {
                if (item.PlatformNotificationId is not uint id)
                {
                    continue;
                }

                activeIds.Add(id);
                if (_locallyDismissedIds.ContainsKey(id))
                {
                    continue;
                }

                var isNewOrUpdated = !_knownSystemToasts.TryGetValue(id, out var prev) || prev != item;
                _knownSystemToasts[id] = item;
                if (raiseEvents && isNewOrUpdated)
                {
                    NotificationReceived?.Invoke(this, item);
                }
            }

            foreach (var existingId in _knownSystemToasts.Keys.ToList())
            {
                if (!activeIds.Contains(existingId) && _knownSystemToasts.TryRemove(existingId, out _))
                {
                    _locallyDismissedIds.TryRemove(existingId, out _);
                    if (raiseEvents)
                    {
                        NotificationRemoved?.Invoke(this, existingId);
                    }
                }
            }
        }
        catch
        {
            // Ignore transient Windows database lock while WPN commits a toast.
        }
        finally
        {
            _syncLock.Release();
        }
    }

    internal static List<NotificationItem> ReadSystemToastsFromSqlite(string dbPath)
    {
        var items = new List<NotificationItem>();
        if (!File.Exists(dbPath))
        {
            return items;
        }

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };

        using var conn = new SqliteConnection(csb.ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, h.PrimaryId, n.Payload, n.ArrivalTime
            FROM Notification n
            LEFT JOIN NotificationHandler h ON n.HandlerId = h.RecordId
            WHERE n.Type = 'toast'
              AND (n.ExpiryTime IS NULL OR n.ExpiryTime = 0 OR n.ExpiryTime > @nowFileTime)
            ORDER BY n.ArrivalTime DESC
            LIMIT 40
            """;
        cmd.Parameters.AddWithValue("@nowFileTime", DateTimeOffset.UtcNow.ToFileTime());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var rawId = reader.GetInt64(0);
            if (rawId < 0 || rawId > uint.MaxValue)
            {
                continue;
            }

            var primaryId = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (IsBrowserHandler(primaryId))
            {
                // System notifications only — exclude web browser push notifications per user preference.
                continue;
            }

            string? payloadXml = null;
            if (!reader.IsDBNull(2))
            {
                var rawPayload = reader.GetValue(2);
                payloadXml = rawPayload switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
                    string str => str.TrimEnd('\0'),
                    _ => null
                };
            }

            var arrivalFileTime = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
            var parsed = ParseWpnToastPayload((uint)rawId, primaryId, payloadXml, arrivalFileTime);
            if (parsed is not null)
            {
                items.Add(parsed);
            }
        }

        return items;
    }

    internal static NotificationItem? ParseWpnToastPayload(uint id, string? primaryId, string? payloadXml, long arrivalFileTime)
    {
        var appName = FormatSystemAppName(primaryId);
        var texts = new List<string>();
        string? actionLabel = null;

        if (!string.IsNullOrWhiteSpace(payloadXml))
        {
            try
            {
                var doc = XDocument.Parse(payloadXml);
                texts = doc.Descendants()
                    .Where(e => string.Equals(e.Name.LocalName, "text", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Value.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                actionLabel = doc.Descendants()
                    .Where(e => string.Equals(e.Name.LocalName, "action", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Attribute("content")?.Value?.Trim())
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            }
            catch
            {
                // Malformed payload XML; fallback to app title.
            }
        }

        if (texts.Count == 0 && string.Equals(appName, "Windows System", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = texts.Count > 0 ? texts[0] : appName;
        var body = texts.Count > 1 ? string.Join(" — ", texts.Skip(1)) : string.Empty;

        DateTimeOffset timestamp;
        try
        {
            timestamp = arrivalFileTime > 0
                ? DateTimeOffset.FromFileTime(arrivalFileTime).ToUniversalTime()
                : DateTimeOffset.UtcNow;
        }
        catch
        {
            timestamp = DateTimeOffset.UtcNow;
        }

        return new NotificationItem(
            Id: $"sys-{id}",
            AppName: appName,
            Title: title,
            Body: body,
            TimestampUtc: timestamp,
            Read: false,
            Dismissed: false,
            ActionLabel: actionLabel,
            PlatformNotificationId: id);
    }

    internal static bool IsBrowserHandler(string? primaryId)
    {
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return false;
        }

        return primaryId.Equals("Chrome", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("Google.Chrome", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("Microsoft.MicrosoftEdge", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("Opera", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("Brave", StringComparison.OrdinalIgnoreCase)
            || primaryId.Contains("Firefox", StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatSystemAppName(string? primaryId)
    {
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            return "Windows System";
        }

        var raw = primaryId.Trim();
        var bang = raw.IndexOf('!');
        if (bang > 0)
        {
            raw = raw[..bang];
        }

        var underscore = raw.IndexOf('_');
        if (underscore > 0)
        {
            raw = raw[..underscore];
        }

        if (raw.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase)) return "Snipping Tool";
        if (raw.Contains("SecHealthUI", StringComparison.OrdinalIgnoreCase) || raw.Contains("Defender", StringComparison.OrdinalIgnoreCase)) return "Windows Security";
        if (raw.Contains("DesktopAppInstaller", StringComparison.OrdinalIgnoreCase)) return "App Installer";
        if (raw.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase)) return "Windows Update";
        if (raw.Contains("WindowsTerminal", StringComparison.OrdinalIgnoreCase)) return "Windows Terminal";
        if (raw.Contains("Windows.SystemToast", StringComparison.OrdinalIgnoreCase) || raw.Contains("Windows.Shell", StringComparison.OrdinalIgnoreCase)) return "Windows System";
        if (raw.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["Microsoft.".Length..];
        }
        else if (raw.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["Windows.".Length..];
        }

        var slash = Math.Max(raw.LastIndexOf('\\'), raw.LastIndexOf('/'));
        if (slash >= 0 && slash < raw.Length - 1)
        {
            raw = Path.GetFileNameWithoutExtension(raw[(slash + 1)..]);
        }

        return string.IsNullOrWhiteSpace(raw) ? "Windows System" : raw.Replace('.', ' ');
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id;
            return true;
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
            if (match is not null && !IsBrowserHandler(match.AppInfo?.AppUserModelId))
            {
                NotificationReceived?.Invoke(this, MapNotification(match));
            }
        }
        catch
        {
            // Ignore transient WinRT read race.
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

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _wpnWatcher?.Dispose();
        _syncLock.Dispose();
    }
}
