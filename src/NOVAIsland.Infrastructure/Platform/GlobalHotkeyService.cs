using System.Runtime.InteropServices;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Registers global Windows hotkeys via Win32 RegisterHotKey on a dedicated message-only window.
/// </summary>
public sealed class GlobalHotkeyService : IHotkeyService, IDisposable
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly Dictionary<int, ShortcutAction> _registeredIds = [];
    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _wndProc;
    private IntPtr _prevWndProc;
    private int _nextId = 100;

    public event EventHandler<ShortcutAction>? ShortcutTriggered;

    public void InitializeMessageWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        _hwnd = Win32Interop.CreateWindowEx(0, "STATIC", "DOOMIslandHotkeySink", 0, 0, 0, 0, 0, Win32Interop.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _wndProc = WndProc;
        var ptr = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _prevWndProc = Win32Interop.SetWindowLongPtr(_hwnd, -4 /* GWLP_WNDPROC */, ptr);
    }

    public void RegisterBindings(IReadOnlyList<ShortcutBinding> bindings)
    {
        InitializeMessageWindow();
        UnregisterAll();

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        foreach (var binding in bindings)
        {
            if (!TryParseHotkey(binding.Keys, out var mods, out var vk))
            {
                continue;
            }

            var id = _nextId++;
            if (Win32Interop.RegisterHotKey(_hwnd, id, mods | MOD_NOREPEAT, vk))
            {
                _registeredIds[id] = binding.Action;
            }
        }
    }

    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        foreach (var id in _registeredIds.Keys)
        {
            Win32Interop.UnregisterHotKey(_hwnd, id);
        }
        _registeredIds.Clear();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Interop.WM_HOTKEY && _registeredIds.TryGetValue(wParam.ToInt32(), out var action))
        {
            ShortcutTriggered?.Invoke(this, action);
            return IntPtr.Zero;
        }

        return Win32Interop.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    public static bool TryParseHotkey(string keys, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(keys))
        {
            return false;
        }

        var parts = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CONTROL;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "SPACE":
                    virtualKey = 0x20;
                    break;
                case ",":
                case "OEMCOMMA":
                    virtualKey = 0xBC;
                    break;
                case ".":
                    virtualKey = 0xBE;
                    break;
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                    {
                        virtualKey = char.ToUpperInvariant(part[0]);
                    }
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out var fn) && fn is >= 1 and <= 12)
                    {
                        virtualKey = (uint)(0x70 + fn - 1);
                    }
                    break;
            }
        }

        return virtualKey != 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}

