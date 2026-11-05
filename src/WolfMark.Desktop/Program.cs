using Avalonia;
using WolfMark.App.Infrastructure;
using WolfMark.Desktop.Infrastructure;

namespace WolfMark.Desktop;

/// <summary>Entry point: one instance per data folder (a later start hands over its file), then the Avalonia desktop lifetime.</summary>
internal static class Program
{
    internal static SingleInstance? Instance { get; private set; }

    /// <summary>Set by the settings (new notes folder, language): the app starts again after it closed.</summary>
    internal static bool RestartRequested { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Instance = SingleInstance.TryAcquire(InstanceIdentity.DataDirectory(LaunchOptions.Current), LaunchOptions.Current.OpenFile);
        if (Instance is null)
        {
            return 0; // the running instance opens the file, or its window comes forward
        }

        Strings.Source = DesktopStrings.Get;
        try
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance.Dispose(); // the new process must find the lock free
            if (RestartRequested && Environment.ProcessPath is { } exe)
            {
                System.Diagnostics.Process.Start(exe, args);
            }
        }
    }
}
