using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using WolfMark.Core.Diagnostics;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// M4-09: the editor's first use of Win2D loads its DLLs, creates the shared Direct3D device and DirectWrite's system
/// font collection – on the UI thread, before the first frame. Started in Main on a pool thread, that work overlaps
/// the Windows App SDK start and the window construction instead. Win2D's shared device and text objects are
/// thread-safe (multithreaded D2D factory); the editor later gets the same shared device.
/// </summary>
internal static class Win2DWarmUp
{
    public static void Start() => _ = Task.Run(() =>
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            using var format = new CanvasTextFormat { FontFamily = "Segoe UI Variable Text", FontSize = 15 };
            using var layout = new CanvasTextLayout(device, "WolfMark", format, 200, 40);
            _ = layout.LayoutBounds;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // Only a head start: the editor creates what it needs itself (and reports real failures).
        }
        finally
        {
            StartupTimeline.WarmUpFinished();
        }
    });
}
