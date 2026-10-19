using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace WolfMark.App.Infrastructure;

/// <summary>Installed font families, enumerated through GDI (the pitch flag tells monospaced fonts apart).</summary>
internal static unsafe class InstalledFonts
{
    private const byte FixedPitch = 1; // FIXED_PITCH in the low bits of lfPitchAndFamily

    private static Task<IReadOnlyList<string>>? s_monospaced;

    /// <summary>Monospaced families, sorted; enumerated once per run on the thread pool.</summary>
    public static Task<IReadOnlyList<string>> MonospacedAsync() =>
        s_monospaced ??= Task.Run(EnumerateMonospaced);

    private static IReadOnlyList<string> EnumerateMonospaced()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hdc = PInvoke.GetDC(HWND.Null);
        var handle = GCHandle.Alloc(names);
        try
        {
            // Empty face name + DEFAULT_CHARSET: one callback per family and charset.
            var filter = new LOGFONTW { lfCharSet = FONT_CHARSET.DEFAULT_CHARSET };
            _ = PInvoke.EnumFontFamiliesEx(hdc, &filter, &OnFont, (LPARAM)GCHandle.ToIntPtr(handle), 0);
        }
        finally
        {
            handle.Free();
            _ = PInvoke.ReleaseDC(HWND.Null, hdc);
        }

        return names.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnFont(LOGFONTW* font, TEXTMETRICW* metrics, uint fontType, LPARAM state)
    {
        var name = font->lfFaceName.ToString();
        // '@' families are the vertical variants of CJK fonts.
        if ((font->lfPitchAndFamily & 3) == FixedPitch && name.Length > 0 && name[0] != '@'
            && GCHandle.FromIntPtr(state.Value).Target is HashSet<string> names)
        {
            names.Add(name);
        }

        return 1; // continue
    }
}
