namespace WolfMark.App.Infrastructure;

/// <summary>
/// Command-line switches. <c>--demo &lt;dir&gt;</c> runs on isolated demo data (notes, Claude Code transcripts,
/// settings and index all under that folder – the user's notes and settings are never touched);
/// <c>--screenshots &lt;dir&gt;</c> additionally captures the README screenshots there and exits.
/// </summary>
internal sealed record LaunchOptions(string? DemoDirectory, string? ScreenshotDirectory)
{
    public static LaunchOptions Current { get; } = Parse(Environment.GetCommandLineArgs());

    public bool IsDemo => DemoDirectory is not null;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? demo = null, shots = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--demo", StringComparison.OrdinalIgnoreCase))
            {
                demo = Path.GetFullPath(args[i + 1]);
            }
            else if (string.Equals(args[i], "--screenshots", StringComparison.OrdinalIgnoreCase))
            {
                shots = Path.GetFullPath(args[i + 1]);
            }
        }

        // Screenshots only ever come from demo data.
        if (shots is not null && demo is null)
        {
            demo = Path.Combine(Path.GetTempPath(), "WolfMarkDemo");
        }

        return new LaunchOptions(demo, shots);
    }
}
