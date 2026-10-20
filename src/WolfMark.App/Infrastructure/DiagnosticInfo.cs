using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace WolfMark.App.Infrastructure;

/// <summary>Version and environment for Settings → Diagnostics and bug reports (copied as plain text).</summary>
internal static class DiagnosticInfo
{
    /// <summary>"0.2.3+abc1234" – the commit is added by the SDK when built from git.</summary>
    public static string Version { get; } =
        typeof(DiagnosticInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DiagnosticInfo).Assembly.GetName().Version?.ToString() ?? "?";

    /// <summary>Short form for the UI: the commit hash cut to 7 characters.</summary>
    public static string ShortVersion => Version.IndexOf('+') is var plus and > 0 && Version.Length > plus + 8
        ? Version[..(plus + 8)]
        : Version;

    public static string Build(AppHost host, int noteCount, double displayScale)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"WolfMark {Version}");
        sb.AppendLine(inv, $"{RuntimeInformation.FrameworkDescription}, {(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT")}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine(inv, $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine(inv, $"Display scale {displayScale * 100:0} %, UI language {CultureInfo.CurrentUICulture.Name}, formats {CultureInfo.CurrentCulture.Name}");
        sb.AppendLine(inv, $"Notes: {noteCount} in {host.Root}{(LaunchOptions.Current.IsDemo ? " (demo)" : string.Empty)}");
        sb.AppendLine(inv, $"Index: {host.Paths.IndexDatabase} ({FileSize(host.Paths.IndexDatabase)})");
        sb.Append(inv, $"Logs: {host.Paths.LogsDirectory}");
        return sb.ToString();
    }

    private static string FileSize(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0} kB";
        }
        catch (IOException)
        {
            return "–";
        }
    }
}
