using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WolfMark.Core.Settings;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// A system-wide shortcut (RegisterHotKey) delivered to the main window – also while it is hidden in the
/// notification area. WM_HOTKEY arrives through a subclass of the window, like the tray icon's messages. Windows
/// gives the process the right to take the foreground with the hot key, so the window it opens comes to the front.
/// </summary>
internal sealed unsafe class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x574E; // "WN"; an application's ids are 0x0000–0xBFFF
    private const nuint SubclassId = 0x574E;

    private static GlobalHotkey? s_current;
    private readonly HWND _hwnd;
    private bool _registered;

    public GlobalHotkey(nint hwnd)
    {
        _hwnd = new HWND(hwnd);
        s_current = this;
        PInvoke.SetWindowSubclass(_hwnd, &SubclassProc, SubclassId, 0);
    }

    /// <summary>Raised on the UI thread when the shortcut is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>The shortcut now registered, null when none.</summary>
    public KeyChord? Current { get; private set; }

    /// <summary>Registers <paramref name="chord"/> instead of the previous one; false when another app already has it.</summary>
    public bool Register(KeyChord chord)
    {
        if (_registered && Current == chord)
        {
            return true;
        }

        Unregister();
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (chord.Modifiers.HasFlag(ChordModifiers.Control))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Alt))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Shift))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Windows))
        {
            modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        }

        _registered = PInvoke.RegisterHotKey(_hwnd, HotkeyId, modifiers, (uint)chord.VirtualKey);
        Current = _registered ? chord : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
            Current = null;
        }
    }

    public void Dispose()
    {
        Unregister();
        PInvoke.RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
        if (s_current == this)
        {
            s_current = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT SubclassProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint refData)
    {
        if (msg == PInvoke.WM_HOTKEY && wParam.Value == HotkeyId && s_current is { } hotkey)
        {
            hotkey.Pressed?.Invoke(hotkey, EventArgs.Empty);
            return new LRESULT(0);
        }

        return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
