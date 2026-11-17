namespace WolfMark.App.Infrastructure;

/// <summary>
/// Synchronous copy of every unhandled exception in <c>logs/crash.txt</c> (both apps): the process may end right
/// after (AppDomain.UnhandledException), before the logger gets to write.
/// </summary>
internal static class CrashFile
{
    public static void Write(Exception? ex, string source, string? detail = null)
    {
        try
        {
            var dir = LaunchOptions.Current.DemoDirectory is { } demo
                ? Path.Combine(demo, "local", "logs")
                : WolfMark.Storage.Settings.LocalPaths.Default.LogsDirectory;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.txt"), Format(DateTime.Now, ex, source, detail));
        }
        catch (Exception)
        {
            // nothing left to report to
        }
    }

    internal static string Format(DateTime at, Exception? ex, string source, string? detail)
    {
        var what = ex?.ToString() ?? "(no exception object)";
        var extra = detail is null ? string.Empty : $" ({detail})";
        return $"{at:yyyy-MM-dd HH:mm:ss.fff} [{source}]{extra} {what}{Environment.NewLine}{Environment.NewLine}";
    }
}
