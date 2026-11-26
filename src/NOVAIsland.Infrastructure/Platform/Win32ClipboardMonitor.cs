using System.Runtime.InteropServices;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Services;
using NOVAIsland.Infrastructure.Caching;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NOVAIsland.Infrastructure.Platform;

public sealed class Win32ClipboardMonitor : IClipboardMonitorService, IDisposable
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIB = 8;

    private readonly StorageFootprintService _storage;
    private readonly Func<AppSettings> _settingsAccessor;
    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _wndProc;
    private IntPtr _prevWndProc;
    private uint _fmtExcludeMonitor;
    private uint _fmtCanIncludeHistory;
    private bool _suppressNextCapture;

    public event EventHandler<ClipboardItem>? ItemCaptured;

    public Win32ClipboardMonitor(StorageFootprintService storage, Func<AppSettings> settingsAccessor)
    {
        _storage = storage;
        _settingsAccessor = settingsAccessor;
    }

    public void Start()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        _fmtExcludeMonitor = Win32Interop.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        _fmtCanIncludeHistory = Win32Interop.RegisterClipboardFormat("CanIncludeInClipboardHistory");

        _hwnd = Win32Interop.CreateWindowEx(0, "STATIC", "DOOMIslandClipboardSink", 0, 0, 0, 0, 0, Win32Interop.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _wndProc = WndProc;
        _prevWndProc = Win32Interop.SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));
        Win32Interop.AddClipboardFormatListener(_hwnd);
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.RemoveClipboardFormatListener(_hwnd);
            Win32Interop.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    public Task CopyToClipboardAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        _suppressNextCapture = true;
        if (item.Type != ClipboardItemType.Image)
        {
            SetClipboardUnicodeText(item.Content);
        }
        return Task.CompletedTask;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Interop.WM_CLIPBOARDUPDATE)
        {
            _ = HandleClipboardUpdateAsync();
            return IntPtr.Zero;
        }

        return Win32Interop.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    private async Task HandleClipboardUpdateAsync()
    {
        if (_suppressNextCapture)
        {
            _suppressNextCapture = false;
            return;
        }

        var settings = _settingsAccessor();
        if (!settings.Clipboard.Enabled)
        {
            return;
        }

        // Respect Windows standard password-manager exclusion format
        if (settings.Clipboard.ExcludeSensitive &&
            _fmtExcludeMonitor != 0 &&
            Win32Interop.IsClipboardFormatAvailable(_fmtExcludeMonitor))
        {
            return;
        }

        var ownerHwnd = Win32Interop.GetClipboardOwner();
        if (ownerHwnd == IntPtr.Zero)
        {
            ownerHwnd = Win32Interop.GetForegroundWindow();
        }

        var processName = Win32Interop.GetProcessNameFromWindow(ownerHwnd);
        if (SensitiveContentDetector.IsExcludedApp(processName, settings.Clipboard.ExcludedApps))
        {
            return;
        }

        if (settings.Privacy.PauseInPrivateBrowsing)
        {
            var title = Win32Interop.GetForegroundWindowTitle();
            if (title.Contains("InPrivate", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Incognito", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Private Browsing", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        // Retry OpenClipboard briefly in case source app still holds lock
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(_hwnd))
            {
                try
                {
                    if (_fmtCanIncludeHistory != 0 &&
                        settings.Clipboard.ExcludeSensitive &&
                        IsCanIncludeDisabled())
                    {
                        return;
                    }

                    if (Win32Interop.IsClipboardFormatAvailable(CF_UNICODETEXT))
                    {
                        var text = ReadUnicodeText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var isSensitive = SensitiveContentDetector.LooksSensitive(text);
                            if (settings.Clipboard.ExcludeSensitive && isSensitive)
                            {
                                return;
                            }

                            var kind = SensitiveContentDetector.ClassifyText(text);
                            var preview = text.Split('\n', 2)[0].Trim();
                            if (preview.Length > 48)
                            {
                                preview = preview[..48] + "…";
                            }

                            var item = new ClipboardItem(
                                Id: $"clip-{Guid.NewGuid():N}",
                                Type: kind,
                                Content: text,
                                PreviewTitle: preview,
                                Language: kind == ClipboardItemType.Code ? "Code" : null,
                                ImagePath: null,
                                SourceApp: string.IsNullOrWhiteSpace(processName) ? "Windows" : processName,
                                CapturedAtUtc: DateTimeOffset.UtcNow,
                                Pinned: false,
                                Sensitive: isSensitive);

                            ItemCaptured?.Invoke(this, item);
                            return;
                        }
                    }

                    if (settings.Clipboard.CaptureImages && Win32Interop.IsClipboardFormatAvailable(CF_DIB))
                    {
                        var dibBytes = ReadDibBytes();
                        if (dibBytes is { Length: > 40 })
                        {
                            var path = await SaveDibAsPngAsync(dibBytes);
                            if (path is not null)
                            {
                                var kb = Math.Max(1, new FileInfo(path).Length / 1024);
                                var item = new ClipboardItem(
                                    Id: $"clip-{Guid.NewGuid():N}",
                                    Type: ClipboardItemType.Image,
                                    Content: $"PNG · {kb} KB",
                                    PreviewTitle: Path.GetFileName(path),
                                    Language: null,
                                    ImagePath: path,
                                    SourceApp: string.IsNullOrWhiteSpace(processName) ? "Windows" : processName,
                                    CapturedAtUtc: DateTimeOffset.UtcNow,
                                    Pinned: false,
                                    Sensitive: false);

                                ItemCaptured?.Invoke(this, item);
                            }
                        }
                    }
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            await Task.Delay(25);
        }
    }

    private bool IsCanIncludeDisabled()
    {
        var handle = GetClipboardData(_fmtCanIncludeHistory);
        if (handle == IntPtr.Zero) return false;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return false;
        try
        {
            return Marshal.ReadInt32(ptr) == 0;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static string? ReadUnicodeText()
    {
        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? ReadDibBytes()
    {
        var handle = GetClipboardData(CF_DIB);
        if (handle == IntPtr.Zero) return null;
        var size = (int)GlobalSize(handle);
        if (size <= 0 || size > 32 * 1024 * 1024) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var data = new byte[size];
            Marshal.Copy(ptr, data, 0, size);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private async Task<string?> SaveDibAsPngAsync(byte[] dib)
    {
        try
        {
            var headerSize = BitConverter.ToInt32(dib, 0);
            var bpp = BitConverter.ToInt16(dib, 14);
            var clrUsed = headerSize >= 36 ? BitConverter.ToInt32(dib, 32) : 0;
            var paletteBytes = bpp <= 8 ? (clrUsed > 0 ? clrUsed : (1 << bpp)) * 4 : 0;
            var pixelOffset = 14 + headerSize + paletteBytes;
            var fileSize = 14 + dib.Length;

            using var bmpStream = new MemoryStream(fileSize);
            using var bw = new BinaryWriter(bmpStream);
            bw.Write((byte)'B');
            bw.Write((byte)'M');
            bw.Write(fileSize);
            bw.Write(0);
            bw.Write(pixelOffset);
            bw.Write(dib);
            bw.Flush();
            bmpStream.Position = 0;

            var ras = bmpStream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var fileName = $"Clip_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var outPath = Path.Combine(_storage.ClipboardImagesDir, fileName);

            await using var fileStream = File.Create(outPath);
            var outRas = fileStream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outRas);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync();
            return outPath;
        }
        catch
        {
            return null;
        }
    }

    private void SetClipboardUnicodeText(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            if (OpenClipboard(_hwnd))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var hGlobal = GlobalAlloc(0x0042 /* GHND */, (UIntPtr)bytes);
                    if (hGlobal != IntPtr.Zero)
                    {
                        var ptr = GlobalLock(hGlobal);
                        Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                        GlobalUnlock(hGlobal);
                        SetClipboardData(CF_UNICODETEXT, hGlobal);
                    }
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    public void Dispose() => Stop();
}
