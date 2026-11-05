using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// "Open .md files in WolfMark" for the unpackaged app, per user (HKCU, no admin): a ProgID with the open command,
/// an entry in the <c>.md</c> "Open with" list and the capabilities Default apps lists. Each copy of the app
/// (installed, development build) registers under its own ProgID, named after its folder. Windows does not let an
/// app make itself the default – the user confirms it in Default apps (<see cref="OpenDefaultAppsAsync"/>).
/// The registry is the source of truth.
/// </summary>
internal static class FileAssociation
{
    private const string Extension = ".md";
    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    private static string Id => InstanceIdentity.Hash(Path.GetDirectoryName(Exe)!);

    private static string ProgId => "WolfMark.md." + Id;

    private static string CapabilitiesKey => @"Software\WolfMark\Capabilities\" + Id;

    private static string RegisteredName => "WolfMark." + Id;

    /// <summary>The command Explorer runs: this exe with the file (and the demo data, so a demo build opens in its demo).</summary>
    private static string Command => LaunchOptions.Current.DemoDirectory is { } demo
        ? $"\"{Exe}\" --demo \"{demo}\" \"%1\""
        : $"\"{Exe}\" \"%1\"";

    /// <summary>Registered, and for this exe (a moved app has to register again).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command");
            return key?.GetValue(null) is string command && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static unsafe void Set(bool enabled)
    {
        if (enabled)
        {
            Register();
        }
        else
        {
            Unregister();
        }

        // Explorer reads associations once; this makes it read them again.
        PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST, null, null);
    }

    /// <summary>Settings → Default apps, on WolfMark's page when Windows knows it.</summary>
    public static Task OpenDefaultAppsAsync() => Task.Run(() =>
    {
        var uri = "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(RegisteredName);
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    });

    private static void Register()
    {
        var name = "WolfMark (" + Path.GetDirectoryName(Exe) + ")";
        using (var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId, writable: true))
        {
            progId.SetValue(null, "Markdown");
            progId.SetValue("FriendlyTypeName", "Markdown");
            using (var icon = progId.CreateSubKey("DefaultIcon", writable: true))
            {
                icon.SetValue(null, $"\"{Exe}\",0");
            }

            using var command = progId.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(null, Command);
        }

        using (var openWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Extension + @"\OpenWithProgids", writable: true))
        {
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey, writable: true))
        {
            capabilities.SetValue("ApplicationName", name);
            capabilities.SetValue("ApplicationDescription", "WolfMark – daily notes in Markdown");
            using var files = capabilities.CreateSubKey("FileAssociations", writable: true);
            files.SetValue(Extension, ProgId);
        }

        using var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey, writable: true);
        registered.SetValue(RegisteredName, CapabilitiesKey);
    }

    private static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, throwOnMissingSubKey: false);
        using (var openWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Extension + @"\OpenWithProgids", writable: true))
        {
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);
        using var registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true);
        registered?.DeleteValue(RegisteredName, throwOnMissingValue: false);
    }
}
