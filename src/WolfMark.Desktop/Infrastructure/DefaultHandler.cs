using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WolfMark.App.Infrastructure;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// "Open .md files in WolfMark" on macOS and Linux (the Windows app has Infrastructure/FileAssociation.cs).
/// macOS: the app bundle declares Markdown documents (Info.plist, tools/package-desktop.sh) and Launch Services
/// makes it the default handler – only a bundled app (WolfMark.app) can be one. Linux: a desktop entry of this copy
/// of the app (its own name per folder, so a development build does not replace the installed one) set as the
/// default for text/markdown with xdg-mime. The system's state is the truth. Call off the UI thread.
/// </summary>
internal static partial class DefaultHandler
{
    private const string BundleId = "net.vlko.wolfmark";
    private const string TextEditBundleId = "com.apple.TextEdit";
    private static readonly string[] ContentTypes = ["net.daringfireball.markdown", "public.markdown"];
    private static readonly string[] MimeTypes = ["text/markdown", "text/x-markdown"];

    /// <summary>False when this run cannot be a default handler (macOS outside an app bundle).</summary>
    public static bool IsSupported => OperatingSystem.IsLinux() || (OperatingSystem.IsMacOS() && AppContext.BaseDirectory.Contains(".app/Contents/", StringComparison.Ordinal));

    public static bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return IsSupported && string.Equals(MacDefaultHandler(ContentTypes[0]), BundleId, StringComparison.OrdinalIgnoreCase);
            }

            return OperatingSystem.IsLinux()
                && File.Exists(DesktopFile)
                && string.Equals(Run("xdg-mime", "query", "default", MimeTypes[0])?.Trim(), DesktopFileName, StringComparison.Ordinal);
        }
    }

    /// <summary>Makes WolfMark the default for Markdown files, or gives them back (TextEdit / the system's choice).</summary>
    public static void Set(bool enabled)
    {
        if (OperatingSystem.IsMacOS())
        {
            var handler = enabled ? BundleId : TextEditBundleId;
            MacSetDefaultHandler(ContentTypes[0], handler);
            foreach (var type in ContentTypes.Skip(1))
            {
                try
                {
                    MacSetDefaultHandler(type, handler); // a newer name of the type, not known on every macOS
                }
                catch (IOException)
                {
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (enabled)
            {
                LinuxRegister();
            }
            else
            {
                LinuxUnregister();
            }
        }
    }

    // ---------------------------------------------------------------- Linux

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    private static string ApplicationsDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
            ? data
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "applications");

    /// <summary>"wolfmark-3f2a….desktop": per copy of the app, after its folder.</summary>
    private static string DesktopFileName => "wolfmark-" + InstanceIdentity.Hash(Path.GetDirectoryName(Exe)!) + ".desktop";

    private static string DesktopFile => Path.Combine(ApplicationsDirectory, DesktopFileName);

    private static void LinuxRegister()
    {
        var folder = Path.GetDirectoryName(Exe)!;
        var icon = Path.Combine(folder, "wolfmark.png");
        var demo = LaunchOptions.Current.DemoDirectory is { } d ? $" --demo \"{d}\"" : string.Empty;
        var entry = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Name=WolfMark")
            .AppendLine("Comment=Daily notes in Markdown")
            .AppendLine($"Exec=\"{Exe}\"{demo} %f")
            .AppendLine("Icon=" + (File.Exists(icon) ? icon : "wolfmark"))
            .AppendLine("Terminal=false")
            .AppendLine("Categories=Office;Utility;")
            .AppendLine("MimeType=" + string.Join(';', MimeTypes) + ";")
            .AppendLine("StartupWMClass=WolfMark.Desktop")
            .ToString();
        Directory.CreateDirectory(ApplicationsDirectory);
        File.WriteAllText(DesktopFile, entry);
        Run("update-desktop-database", ApplicationsDirectory);
        foreach (var mime in MimeTypes)
        {
            if (Run("xdg-mime", "default", DesktopFileName, mime) is null)
            {
                throw new IOException("xdg-mime is not available (package xdg-utils).");
            }
        }
    }

    /// <summary>Removes the desktop entry and its lines in mimeapps.list; the desktop picks another handler.</summary>
    private static void LinuxUnregister()
    {
        File.Delete(DesktopFile);
        Run("update-desktop-database", ApplicationsDirectory);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c
            ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var list = Path.Combine(config, "mimeapps.list");
        if (File.Exists(list))
        {
            var lines = File.ReadAllLines(list).Where(l => !l.EndsWith("=" + DesktopFileName, StringComparison.Ordinal) && !l.EndsWith("=" + DesktopFileName + ";", StringComparison.Ordinal));
            File.WriteAllLines(list, lines);
        }
    }

    /// <summary>Standard output of a tool, null when it is missing or failed.</summary>
    private static string? Run(string tool, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- macOS (Launch Services)

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8
    private const uint AllRoles = 0xFFFFFFFF; // kLSRolesAll

    private static void MacSetDefaultHandler(string contentType, string bundleId)
    {
        var type = CFString(contentType);
        var bundle = CFString(bundleId);
        try
        {
            var status = LSSetDefaultRoleHandlerForContentType(type, AllRoles, bundle);
            if (status != 0)
            {
                throw new IOException($"Launch Services refused the change ({status}).");
            }
        }
        finally
        {
            CFRelease(bundle);
            CFRelease(type);
        }
    }

    private static string? MacDefaultHandler(string contentType)
    {
        var type = CFString(contentType);
        try
        {
            var handler = LSCopyDefaultRoleHandlerForContentType(type, AllRoles);
            if (handler == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var buffer = new byte[512];
                return CFStringGetCString(handler, buffer, buffer.Length, Utf8)
                    ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length)
                    : null;
            }
            finally
            {
                CFRelease(handler);
            }
        }
        finally
        {
            CFRelease(type);
        }
    }

    private static IntPtr CFString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        return CFStringCreateWithCString(IntPtr.Zero, bytes, Utf8);
    }

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] value, uint encoding);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr value, [Out] byte[] buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(IntPtr contentType, uint role, IntPtr handlerBundleId);

    [LibraryImport(CoreServices)]
    private static partial IntPtr LSCopyDefaultRoleHandlerForContentType(IntPtr contentType, uint role);
}
