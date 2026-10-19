using Avalonia;
using WolfMark.App.Infrastructure;
using WolfMark.Desktop.Infrastructure;

namespace WolfMark.Desktop;

/// <summary>Entry point: one instance per data folder, then the Avalonia desktop lifetime.</summary>
internal static class Program
{
    internal static SingleInstance? Instance { get; private set; }

    /// <summary>Set by the settings (new notes folder, language): the app starts again after it closed.</summary>
    internal static bool RestartRequested { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var data = LaunchOptions.Current.DemoDirectory is { } demo
            ? Path.Combine(demo, "local")
            : WolfMark.Storage.Settings.LocalPaths.Default.BaseDirectory;
        Instance = SingleInstance.TryAcquire(data);
        if (Instance is null)
        {
            return 0; // the running window was asked to come forward
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
