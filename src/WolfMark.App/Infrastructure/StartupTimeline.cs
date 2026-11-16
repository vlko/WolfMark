using System.Diagnostics;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Milliseconds since the OS created the process (M4-09: start to typing &lt; 300 ms) – the offset to Main once from
/// <see cref="Process.StartTime"/>, every later point from a stopwatch.
/// </summary>
internal static class StartupTimeline
{
    private static long s_mainTimestamp;
    private static double s_mainMs;

    public static long MainMs => (long)s_mainMs;

    public static void MainEntered()
    {
        s_mainTimestamp = Stopwatch.GetTimestamp();
        using var process = Process.GetCurrentProcess();
        s_mainMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
    }

    public static long Now() => (long)(s_mainMs + Stopwatch.GetElapsedTime(s_mainTimestamp).TotalMilliseconds);
}
