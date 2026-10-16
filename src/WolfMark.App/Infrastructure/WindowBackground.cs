using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Experiment for the resize flicker (BACKLOG, known issue): instead of a system backdrop, give the window
/// class a background brush in the paper colour, so areas exposed while resizing are painted by Windows in
/// the paper colour (not black) and WinUI never composites over a backdrop.
/// </summary>
internal static class WindowBackground
{
    private static HBRUSH _brush;

    public static unsafe void SetClassBackground(nint hwnd, Windows.UI.Color color)
    {
        var brush = PInvoke.CreateSolidBrush(new COLORREF((uint)(color.R | (color.G << 8) | (color.B << 16))));
        PInvoke.SetClassLongPtr(new HWND(hwnd), GET_CLASS_LONG_INDEX.GCLP_HBRBACKGROUND, (nint)brush.Value);
        if (_brush != default)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_brush.Value));
        }

        _brush = brush;
        PInvoke.InvalidateRect(new HWND(hwnd), (RECT?)null, true);
    }
}
