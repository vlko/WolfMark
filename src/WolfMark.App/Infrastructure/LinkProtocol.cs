using Microsoft.Win32;
using WolfMark.Core.Links;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// <c>wolfmark://</c> links (M8-03, ADR 0013) for the unpackaged app, per user (HKCU, no admin): the URL protocol
/// key with the open command of this exe. A scheme has one handler – the copy switched on last owns it. Switching
/// off removes the key only when it is this copy's. The registry is the source of truth.
/// </summary>
internal static class LinkProtocol
{
    private const string KeyPath = @"Software\Classes\" + AppLink.Scheme;

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    /// <summary>The command Windows runs: this exe with the link (and the demo data, so a demo build opens its demo).</summary>
    private static string Command => LaunchCommand.Quoted("%1");

    /// <summary>Registered, and for this exe (another copy, or a moved app, does not count).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
            return key?.GetValue(null) is string command && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(null, "URL:WolfMark");
            key.SetValue("URL Protocol", string.Empty);
            using (var icon = key.CreateSubKey("DefaultIcon", writable: true))
            {
                icon.SetValue(null, $"\"{Exe}\",0");
            }

            using var command = key.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(null, Command);
        }
        else if (IsEnabled)
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
    }
}
