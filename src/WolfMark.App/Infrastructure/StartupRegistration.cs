using Microsoft.Win32;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// "Start with Windows" for the unpackaged app: a value under HKCU\…\Run pointing at this exe with
/// <see cref="Argument"/>. The registry is the source of truth (the user can also turn it off in Task Manager).
/// </summary>
internal static class StartupRegistration
{
    public const string Argument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WolfMark";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\" {Argument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Launched by Windows at sign-in (the Run entry), not by the user.</summary>
    public static bool LaunchedAtStartup => Environment.GetCommandLineArgs().Contains(Argument, StringComparer.OrdinalIgnoreCase);
}
