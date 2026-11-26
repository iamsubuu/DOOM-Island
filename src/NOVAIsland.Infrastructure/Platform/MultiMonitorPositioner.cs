using System.Runtime.InteropServices;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Infrastructure.Platform;

public sealed class MultiMonitorPositioner : IWindowPositioningService
{
    private IntPtr _islandHwnd;

    public void AttachWindow(IntPtr hwnd) => _islandHwnd = hwnd;

    public IReadOnlyList<DisplayMonitorInfo> GetMonitors()
    {
        var results = new List<DisplayMonitorInfo>();
        var idx = 1;

        Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref Win32Interop.RECT _, IntPtr _) =>
        {
            var info = new Win32Interop.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32Interop.MONITORINFOEX>() };
            if (Win32Interop.GetMonitorInfo(hMonitor, ref info))
            {
                var isPrimary = (info.dwFlags & 1) != 0;
                var w = info.rcMonitor.Width;
                var h = info.rcMonitor.Height;
                results.Add(new DisplayMonitorInfo(
                    Id: info.szDevice,
                    DisplayName: $"Display {idx} · {w}×{h}" + (isPrimary ? " (Primary)" : string.Empty),
                    Width: w,
                    Height: h,
                    IsPrimary: isPrimary));
                idx++;
            }
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public void ApplyIslandPosition(PositionSettings settings, int widthDip, int heightDip) =>
        ApplyIslandPosition(settings, widthDip, heightDip, flushTopEdge: true);

    public void ApplyIslandPosition(PositionSettings settings, int widthDip, int heightDip, bool flushTopEdge)
    {
        if (_islandHwnd == IntPtr.Zero)
        {
            return;
        }

        var bounds = ResolveTargetMonitorRect(settings, out var scale);
        var widthPx = (int)Math.Round(widthDip * scale);
        var heightPx = (int)Math.Round(heightDip * scale);

        // Static top-center bezel anchor
        var x = bounds.Left + (bounds.Width - widthPx) / 2;
        var y = bounds.Top;

        x = Math.Clamp(x, bounds.Left + 8, Math.Max(bounds.Left + 8, bounds.Right - widthPx - 8));
        y = Math.Clamp(y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - heightPx - 8));

        Win32Interop.SetWindowPos(
            _islandHwnd,
            Win32Interop.HWND_TOPMOST,
            x,
            y,
            widthPx,
            heightPx,
            Win32Interop.SWP_NOACTIVATE);
    }

    public (int X, int Y, double Scale) ComputeScreenPointToCustomPercent(int screenX, int screenY, PositionSettings settings)
    {
        var bounds = ResolveTargetMonitorRect(settings, out var scale);
        var relX = (int)Math.Round((screenX - bounds.Left) / (double)Math.Max(1, bounds.Width) * 100.0);
        var relY = (int)Math.Round((screenY - bounds.Top) / scale);
        return (Math.Clamp(relX, 12, 88), Math.Clamp(relY, 8, 140), scale);
    }

    private static Win32Interop.RECT ResolveTargetMonitorRect(PositionSettings settings, out double scale)
    {
        IntPtr targetHandle = IntPtr.Zero;
        var followCursor = string.Equals(settings.Monitor, "Follow active cursor monitor", StringComparison.OrdinalIgnoreCase);

        if (followCursor && Win32Interop.GetCursorPos(out var pt))
        {
            targetHandle = Win32Interop.MonitorFromPoint(pt, Win32Interop.MONITOR_DEFAULTTONEAREST);
        }

        Win32Interop.RECT selectedRect = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var selectedMonitorHandle = IntPtr.Zero;

        Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Win32Interop.RECT _, IntPtr _) =>
        {
            var info = new Win32Interop.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32Interop.MONITORINFOEX>() };
            if (!Win32Interop.GetMonitorInfo(hMon, ref info))
            {
                return true;
            }

            if (targetHandle != IntPtr.Zero && hMon == targetHandle)
            {
                selectedRect = info.rcMonitor;
                selectedMonitorHandle = hMon;
                return false;
            }

            if (targetHandle == IntPtr.Zero)
            {
                if (string.Equals(settings.Monitor, info.szDevice, StringComparison.OrdinalIgnoreCase))
                {
                    selectedRect = info.rcMonitor;
                    selectedMonitorHandle = hMon;
                    return false;
                }

                if ((info.dwFlags & 1) != 0 && selectedMonitorHandle == IntPtr.Zero)
                {
                    selectedRect = info.rcMonitor;
                    selectedMonitorHandle = hMon;
                }
            }

            return true;
        }, IntPtr.Zero);

        scale = 1.0;
        if (selectedMonitorHandle != IntPtr.Zero &&
            Win32Interop.GetDpiForMonitor(selectedMonitorHandle, Win32Interop.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
        {
            scale = Math.Max(1.0, dpiX / 96.0);
        }

        return selectedRect;
    }
}
