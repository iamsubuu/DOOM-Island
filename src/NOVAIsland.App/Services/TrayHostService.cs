using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;

namespace NOVAIsland.App.Services;

public sealed class TrayHostService : ITrayService, IDisposable
{
    private TaskbarIcon? _trayIcon;
    private IntPtr _hIcon;

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenFullscreenLyricsRequested;
    public event EventHandler<IslandMode>? SwitchModeRequested;
    public event EventHandler? ToggleIslandVisibilityRequested;
    public event EventHandler? ExitRequested;

    public void Initialize(string appName)
    {
        if (_trayIcon is not null)
        {
            UpdateTooltip(appName);
            return;
        }

        _hIcon = CreateGeneratedTrayIcon();
        var flyout = new MenuFlyout();

        AddItem(flyout, "Open Dashboard", () => OpenDashboardRequested?.Invoke(this, EventArgs.Empty));
        AddItem(flyout, "Settings", () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        AddItem(flyout, "Fullscreen Lyrics", () => OpenFullscreenLyricsRequested?.Invoke(this, EventArgs.Empty));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(flyout, "Show / Hide Island", () => ToggleIslandVisibilityRequested?.Invoke(this, EventArgs.Empty));
        AddItem(flyout, "Compact Mode", () => SwitchModeRequested?.Invoke(this, IslandMode.Compact));
        AddItem(flyout, "Clipboard History", () => SwitchModeRequested?.Invoke(this, IslandMode.Clipboard));
        AddItem(flyout, "Timer & Pomodoro", () => SwitchModeRequested?.Invoke(this, IslandMode.Timer));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(flyout, "Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = appName,
            ContextFlyout = flyout,
            ContextMenuMode = ContextMenuMode.SecondWindow
        };
        if (_hIcon != IntPtr.Zero)
        {
            _trayIcon.UpdateIcon(System.Drawing.Icon.FromHandle(_hIcon));
        }
        _trayIcon.ForceCreate();
    }

    public void UpdateTooltip(string text)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = text;
        }
    }

    public void DisposeTray()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    public void Dispose() => DisposeTray();

    private static void AddItem(MenuFlyout flyout, string text, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => onClick();
        flyout.Items.Add(item);
    }

    private static IntPtr CreateGeneratedTrayIcon()
    {
        // 16x16 ARGB Dynamic Island pill icon via pure Win32 CreateIconIndirect
        const int size = 16;
        var pixels = new int[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inBg = (x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5) <= 56;
                var inPill = x >= 4 && x <= 11 && y >= 6 && y <= 9;
                pixels[y * size + x] = inPill
                    ? unchecked((int)0xFF2EE58B)
                    : inBg
                        ? unchecked((int)0xFF06110B)
                        : 0x00000000;
            }
        }

        var hBitmap = CreateBitmap(size, size, 1, 32, pixels);
        var hMask = CreateBitmap(size, size, 1, 1, new byte[size * 4]);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmColor = hBitmap, hbmMask = hMask };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(hBitmap);
            DeleteObject(hMask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, int[] lpBits);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[] lpBits);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
}
