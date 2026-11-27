using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace NOVAIsland.App.Services;

internal static class WindowIconHelper
{
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private static IntPtr _cachedSmallIcon;
    private static IntPtr _cachedBigIcon;

    public static void TryApply(IntPtr hwnd, AppWindow appWindow)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DOOMIsland.ico");
            if (!File.Exists(iconPath))
            {
                return;
            }

            appWindow.SetIcon(iconPath);

            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (_cachedSmallIcon == IntPtr.Zero)
            {
                _cachedSmallIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            }
            if (_cachedBigIcon == IntPtr.Zero)
            {
                _cachedBigIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
            }

            if (_cachedSmallIcon != IntPtr.Zero)
            {
                SendMessageW(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, _cachedSmallIcon);
                SetClassLongPtr(hwnd, GCLP_HICONSM, _cachedSmallIcon);
            }
            if (_cachedBigIcon != IntPtr.Zero)
            {
                SendMessageW(hwnd, WM_SETICON, (IntPtr)ICON_BIG, _cachedBigIcon);
                SetClassLongPtr(hwnd, GCLP_HICON, _cachedBigIcon);
            }
        }
        catch
        {
            // Ignore icon load failures on unsupported window configurations.
        }
    }

    public static void TryApply(AppWindow appWindow) => TryApply(IntPtr.Zero, appWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern uint SetClassLong32(IntPtr hWnd, int nIndex, uint dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetClassLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetClassLong32(hWnd, nIndex, (uint)dwNewLong.ToInt32()));
}
