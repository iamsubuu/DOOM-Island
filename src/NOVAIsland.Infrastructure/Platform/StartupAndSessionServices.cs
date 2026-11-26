using System.Runtime.InteropServices;
using Microsoft.Win32;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Interfaces;

namespace NOVAIsland.Infrastructure.Platform;

public sealed class StartupRegistrationService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppConstants.AppDataFolderName;

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                key.SetValue(ValueName, $"\"{exePath}\" --minimized");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

/// <summary>
/// Monitors workstation lock/unlock transitions via Win32 WTSRegisterSessionNotification.
/// </summary>
public sealed class SessionLockWatcher : IDisposable
{
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;

    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _wndProc;
    private IntPtr _prevWndProc;

    public event EventHandler<bool>? LockStateChanged;

    public void Start()
    {
        if (_hwnd != IntPtr.Zero) return;
        _hwnd = Win32Interop.CreateWindowEx(0, "STATIC", "DOOMIslandSessionSink", 0, 0, 0, 0, 0, Win32Interop.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;

        _wndProc = WndProc;
        _prevWndProc = Win32Interop.SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));
        WTSRegisterSessionNotification(_hwnd, 0 /* NOTIFY_FOR_THIS_SESSION */);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_WTSSESSION_CHANGE)
        {
            var code = wParam.ToInt32();
            if (code == WTS_SESSION_LOCK) LockStateChanged?.Invoke(this, true);
            else if (code == WTS_SESSION_UNLOCK) LockStateChanged?.Invoke(this, false);
        }

        return Win32Interop.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            WTSUnRegisterSessionNotification(_hwnd);
            Win32Interop.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
