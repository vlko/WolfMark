using System.Diagnostics;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Milliseconds since the OS created the process (M4-09: start to typing &lt; 300 ms) – the offset to Main once from
/// <see cref="Process.StartTime"/>, every later point from a stopwatch.
/// </summary>
internal static class StartupTimeline
{
    private static readonly List<(string Name, long Ms)> s_marks = [];
    private static long s_mainTimestamp;
    private static double s_mainMs;
    private static bool s_done;

    public static long MainMs => (long)s_mainMs;

    public static void MainEntered()
    {
        s_mainTimestamp = Stopwatch.GetTimestamp();
        using var process = Process.GetCurrentProcess();
        s_mainMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
    }

    public static long Now() => (long)(s_mainMs + Stopwatch.GetElapsedTime(s_mainTimestamp).TotalMilliseconds);

    /// <summary>A point of the start (UI thread); ignored once <see cref="TakeMarks"/> ran.</summary>
    public static void Mark(string name)
    {
        if (!s_done)
        {
            s_marks.Add((name, Now()));
        }
    }

    /// <summary>For a field initializer (those run before the base class constructor): marks and returns true.</summary>
    public static bool MarkInline(string name)
    {
        Mark(name);
        return true;
    }

    /// <summary>"name +delta" for each mark since the previous one – where the start spends its time.</summary>
    public static string TakeMarks()
    {
        s_done = true;
        var previous = 0L;
        var parts = new List<string>(s_marks.Count);
        foreach (var (name, ms) in s_marks)
        {
            parts.Add($"{name} +{ms - previous}");
            previous = ms;
        }

        s_marks.Clear();
        return string.Join("; ", parts);
    }
}
