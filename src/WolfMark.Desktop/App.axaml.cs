using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Infrastructure;
using WolfMark.Desktop.Theming;
using WolfMark.Desktop.Views;

namespace WolfMark.Desktop;

public sealed partial class App : Application
{
    private readonly List<string> _pendingFiles = [];
    private ILogger? _logger;
    private MainViewModel? _vm;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        UiThread.Initialize(new AvaloniaDispatcher());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal(e.Exception, "TaskScheduler");
            e.SetObserved();
        };

        // macOS hands a file opened in Finder over as an Apple Event, not as an argument – possibly before the window exists.
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs files)
                {
                    foreach (var file in files.Files)
                    {
                        if (file.TryGetLocalPath() is { } path)
                        {
                            UiThread.Post(() => OpenFile(path));
                        }
                    }
                }
            };
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _ = StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var host = await AppHost.CreateAsync(LaunchOptions.Current.DemoDirectory);
            _logger = host.Loggers.CreateLogger<App>();
            DesktopStrings.ApplyLanguage(host.Settings.Current.Language);
            RequestedThemeVariant = host.Settings.Current.Theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
            Palette.Apply(ActualThemeVariant == ThemeVariant.Dark);
            ActualThemeVariantChanged += (_, _) => Palette.Apply(ActualThemeVariant == ThemeVariant.Dark);

            var vm = new MainViewModel(host);
            var window = new MainWindow(vm);
            desktop.MainWindow = window;
            window.Show();
            if (Program.Instance is { } instance)
            {
                instance.Activated += (_, file) => UiThread.Post(() =>
                {
                    if (file is null)
                    {
                        window.BringToFront();
                    }
                    else
                    {
                        OpenFile(file);
                    }
                });
            }

            host.StartBackground();
            await window.InitializeAsync();
            _vm = vm;
            if (LaunchOptions.Current.OpenFile is { } first)
            {
                _pendingFiles.Insert(0, first);
            }

            foreach (var file in _pendingFiles)
            {
                await vm.OpenFileAsync(file);
            }

            _pendingFiles.Clear();
        }
        catch (Exception ex)
        {
            LogFatal(ex, "Start");
            await Console.Error.WriteLineAsync($"WolfMark could not start: {ex}");
            desktop.Shutdown(1);
        }
    }

    /// <summary>A file from another start or from Finder; kept until the window and the last note are there.</summary>
    private void OpenFile(string path)
    {
        if (_vm is null)
        {
            _pendingFiles.Add(path);
        }
        else
        {
            _ = _vm.OpenFileAsync(path);
        }
    }

    private void LogFatal(Exception? ex, string source)
    {
        try
        {
            var dir = LaunchOptions.Current.DemoDirectory is { } demo
                ? Path.Combine(demo, "local", "logs")
                : WolfMark.Storage.Settings.LocalPaths.Default.LogsDirectory;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {ex?.ToString() ?? "(no exception object)"}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // nothing left to report to
        }

        if (_logger is not null && ex is not null)
        {
            LogUnhandled(_logger, source, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception ({Source})")]
    private static partial void LogUnhandled(ILogger logger, string source, Exception ex);
}
