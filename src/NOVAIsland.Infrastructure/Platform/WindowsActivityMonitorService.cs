using System.Runtime.InteropServices;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Infrastructure.Caching;
using Windows.Graphics.Imaging;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Tracks supported local activity sources:
/// - Screenshots saved to Pictures\Screenshots + quick native screen capture
/// - Active browser partial downloads (.crdownload, .part, .tmp) in Downloads folder
/// </summary>
public sealed class WindowsActivityMonitorService : IActivityMonitorService, IDisposable
{
    private readonly StorageFootprintService _storage;
    private readonly List<ActivityItem> _items = [];
    private FileSystemWatcher? _screenshotWatcher;
    private FileSystemWatcher? _downloadsWatcher;

    public event EventHandler<IReadOnlyList<ActivityItem>>? ActivitiesChanged;

    public WindowsActivityMonitorService(StorageFootprintService storage)
    {
        _storage = storage;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var screenshotsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");

        if (Directory.Exists(screenshotsDir))
        {
            _screenshotWatcher = new FileSystemWatcher(screenshotsDir, "*.png")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _screenshotWatcher.Created += OnScreenshotCreated;
        }

        var downloadsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        if (Directory.Exists(downloadsDir))
        {
            _downloadsWatcher = new FileSystemWatcher(downloadsDir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _downloadsWatcher.Created += OnDownloadChanged;
            _downloadsWatcher.Changed += OnDownloadChanged;
            _downloadsWatcher.Renamed += OnDownloadRenamed;
        }

        return Task.CompletedTask;
    }

    public async Task<ActivityItem?> CaptureQuickScreenshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var width = GetSystemMetrics(0 /* SM_CXSCREEN */);
            var height = GetSystemMetrics(1 /* SM_CYSCREEN */);
            if (width <= 0 || height <= 0) return null;

            var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(outDir);
            var fileName = $"DOOM_Capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var outPath = Path.Combine(outDir, fileName);

            var bgra = CapturePrimaryScreenBgra(width, height);
            if (bgra is null) return null;

            using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bgra),
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Ignore);

            await using var fs = File.Create(outPath);
            var ras = fs.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync();

            var kb = Math.Max(1, new FileInfo(outPath).Length / 1024);
            var item = new ActivityItem(
                Id: $"act-snip-{Guid.NewGuid():N}",
                Type: ActivityType.Screenshot,
                Title: "Screenshot captured",
                Subtitle: $"{fileName} · {width}×{height}",
                Progress: 100,
                SpeedText: null,
                SizeText: $"{kb} KB",
                Status: ActivityStatus.Completed,
                FilePath: outPath,
                TimestampUtc: DateTimeOffset.UtcNow);

            UpsertItem(item);
            return item;
        }
        catch
        {
            return null;
        }
    }

    public Task<bool> SaveScreenshotAsAsync(ActivityItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            return Task.FromResult(false);
        }

        var destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, Path.GetFileName(item.FilePath));
        if (!string.Equals(item.FilePath, destPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(item.FilePath, destPath, overwrite: true);
        }

        return Task.FromResult(true);
    }

    private void OnScreenshotCreated(object sender, FileSystemEventArgs e)
    {
        var info = new FileInfo(e.FullPath);
        var kb = info.Exists ? Math.Max(1, info.Length / 1024) : 0;
        UpsertItem(new ActivityItem(
            Id: $"act-fs-{Path.GetFileName(e.FullPath)}",
            Type: ActivityType.Screenshot,
            Title: "Screenshot saved",
            Subtitle: Path.GetFileName(e.FullPath),
            Progress: 100,
            SpeedText: null,
            SizeText: $"{kb} KB",
            Status: ActivityStatus.Completed,
            FilePath: e.FullPath,
            TimestampUtc: DateTimeOffset.UtcNow));
    }

    private void OnDownloadChanged(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath);
        if (!ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".part", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var len = File.Exists(e.FullPath) ? new FileInfo(e.FullPath).Length : 0;
        var mb = len / (1024.0 * 1024.0);
        var displayName = Path.GetFileNameWithoutExtension(e.FullPath);

        UpsertItem(new ActivityItem(
            Id: $"dl-{displayName}",
            Type: ActivityType.Download,
            Title: displayName,
            Subtitle: "Browser Download · Downloads folder",
            Progress: 50, // Total expected size not exposed by filesystem partial files alone
            SpeedText: "Downloading",
            SizeText: $"{mb:0.1} MB received",
            Status: ActivityStatus.Active,
            FilePath: e.FullPath,
            TimestampUtc: DateTimeOffset.UtcNow));
    }

    private void OnDownloadRenamed(object sender, RenamedEventArgs e)
    {
        var oldBase = Path.GetFileNameWithoutExtension(e.OldFullPath);
        var id = $"dl-{oldBase}";
        var existing = _items.FirstOrDefault(x => x.Id == id);
        if (existing is null) return;

        var len = File.Exists(e.FullPath) ? new FileInfo(e.FullPath).Length : 0;
        var mb = len / (1024.0 * 1024.0);
        UpsertItem(existing with
        {
            Title = Path.GetFileName(e.FullPath),
            Progress = 100,
            SpeedText = "Completed",
            SizeText = $"{mb:0.1} MB",
            Status = ActivityStatus.Completed,
            FilePath = e.FullPath,
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    private void UpsertItem(ActivityItem item)
    {
        lock (_items)
        {
            _items.RemoveAll(x => x.Id == item.Id);
            _items.Insert(0, item);
            if (_items.Count > 25)
            {
                _items.RemoveRange(25, _items.Count - 25);
            }
            ActivitiesChanged?.Invoke(this, _items.ToList());
        }
    }

    private static byte[]? CapturePrimaryScreenBgra(int width, int height)
    {
        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return null;
        var hdcMem = CreateCompatibleDC(hdcScreen);
        var hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        var hOld = SelectObject(hdcMem, hBitmap);
        try
        {
            BitBlt(hdcMem, 0, 0, width, height, hdcScreen, 0, 0, 0x00CC0020 /* SRCCOPY */);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            var buffer = new byte[width * height * 4];
            GetDIBits(hdcMem, hBitmap, 0, (uint)height, buffer, ref bmi, 0);
            return buffer;
        }
        finally
        {
            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    public void Dispose()
    {
        _screenshotWatcher?.Dispose();
        _downloadsWatcher?.Dispose();
    }
}
