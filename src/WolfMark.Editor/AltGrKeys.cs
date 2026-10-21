using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace WolfMark.Editor;

/// <summary>
/// AltGr arrives as Ctrl+Alt. On the Slovak layout AltGr types @ { } [ ] \ | € and more, so a Ctrl+Alt chord that
/// the current layout turns into a character is typing, not a shortcut. Asked of the layout itself (ToUnicodeEx;
/// flag 4 leaves a pending dead key alone) – idea from WinUIRichEditor (MIT).
/// </summary>
internal static class AltGrKeys
{
    public static unsafe bool IsTyping(VirtualKey key, bool shift)
    {
        var state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        const byte Down = 0x80;
        state[0x11] = state[0xA2] = state[0x12] = state[0xA5] = Down; // Ctrl, LCtrl, Alt, RAlt (= AltGr)
        if (shift)
        {
            state[0x10] = state[0xA0] = Down;
        }

        var layout = PInvoke.GetKeyboardLayout(0);
        var scan = PInvoke.MapVirtualKeyEx((uint)key, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC, layout);
        var buffer = stackalloc char[8];
        var count = PInvoke.ToUnicodeEx((uint)key, scan, state, new PWSTR(buffer), 8, 4, layout);
        return count > 0 && !char.IsControl(buffer[0]);
    }

    /// <summary>
    /// The character the key types without Ctrl – for shortcuts named by their character: "Ctrl +" is the "1" key on
    /// the Slovak layout and Shift+"=" on the US one.
    /// </summary>
    public static unsafe char CharOf(VirtualKey key, bool shift)
    {
        var state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        if (shift)
        {
            state[0x10] = state[0xA0] = 0x80;
        }

        var layout = PInvoke.GetKeyboardLayout(0);
        var scan = PInvoke.MapVirtualKeyEx((uint)key, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC, layout);
        var buffer = stackalloc char[8];
        var count = PInvoke.ToUnicodeEx((uint)key, scan, state, new PWSTR(buffer), 8, 4, layout);
        return count > 0 ? buffer[0] : '\0';
    }
}
