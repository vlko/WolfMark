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
using WolfMark.Core.Links;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Infrastructure;
using WolfMark.Desktop.Theming;
using WolfMark.Desktop.Views;

namespace WolfMark.Desktop;

public sealed partial class App : Application
{
    private readonly List<string> _pendingFiles = [];
    private readonly List<string> _pendingLinks = [];
    private MainWindow? _window;
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
                if (e is ProtocolActivatedEventArgs protocol)
                {
                    // wolfmark:// on macOS (CFBundleURLTypes): an Apple Event like a file from Finder.
                    var link = protocol.Uri.OriginalString;
                    UiThread.Post(() => OpenRequest(link));
                }
                else if (e is FileActivatedEventArgs files)
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
            var logger = host.Loggers.CreateLogger<App>();
            _logger = logger;
            UiCulture.Apply(host.Settings.Current.Language, host.Settings.Current.DateFormatsFromLanguage, host.Settings.Current.Use24HourTime);
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
            _window = window;
            desktop.MainWindow = window;
            window.Show();
            if (Program.Instance is { } instance)
            {
                instance.Activated += (_, request) => UiThread.Post(() =>
                {
                    var isLink = request is not null && AppLink.LooksLikeLink(request);
                    var file = isLink ? null : request;
                    LogRedirectedActivation(logger, file is not null); // tools/smoke-linux.sh checks for it
                    if (isLink)
                    {
                        OpenRequest(request!);
                    }
                    else if (file is null)
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
            // M4-09: the start note is in the editor and it has the focus (the panels load after it).
            await window.InitializeAsync(() => LogReadyToType(logger, (long)SinceProcessStart().TotalMilliseconds));
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
            if ((LaunchOptions.Current.Link?.ToString() ?? LaunchOptions.Current.InvalidLink) is { } startLink)
            {
                _pendingLinks.Insert(0, startLink);
            }

            foreach (var pending in _pendingLinks.ToList())
            {
                await OpenLinkAsync(pending);
            }

            _pendingLinks.Clear();
            SetDockMenu();
        }
        catch (Exception ex)
        {
            LogFatal(ex, "Start");
            await Console.Error.WriteLineAsync($"WolfMark could not start: {ex}");
            desktop.Shutdown(1);
        }
    }

    /// <summary>A link from another start or from macOS; kept until the window and the last note are there.</summary>
    private void OpenRequest(string link)
    {
        if (_vm is null)
        {
            _pendingLinks.Add(link);
        }
        else
        {
            _ = OpenLinkAsync(link);
        }
    }

    /// <summary>
    /// A <c>wolfmark://</c> link (M8-03) or <c>--quick-note</c>: the quick note window, or the main window on its day,
    /// note or search. One that cannot be parsed is reported.
    /// </summary>
    private async Task OpenLinkAsync(string text)
    {
        if (_window is null || _vm is null || _logger is null)
        {
            return;
        }

        try
        {
            if (!AppLink.TryParse(text, out var link))
            {
                _window.BringToFront();
                _vm.ErrorNotice = Strings.Format(R.Link_Invalid, text);
                return;
            }

            await _window.OpenLinkAsync(link);
            LogLinkOpened(_logger, link.Kind.ToString()); // tools/smoke-linux.sh checks for it
            if (link.Kind == AppLinkKind.New)
            {
                LogQuickNoteShown(_logger);
            }
        }
        catch (Exception ex)
        {
            LogLinkFailed(_logger, ex);
            _vm.ErrorNotice = ex.Message;
        }
    }

    /// <summary>
    /// macOS: New note, Today and Search in the Dock icon's menu – the jump list of the Windows app (M8-03). Linux has
    /// the same as actions of the desktop entry (Infrastructure/DefaultHandler.cs, tools/package-desktop.sh).
    /// </summary>
    private void SetDockMenu()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        NativeMenuItem Item(string text, AppLink link)
        {
            var item = new NativeMenuItem(text);
            item.Click += (_, _) => _ = OpenLinkAsync(link.ToString());
            return item;
        }

        NativeDock.SetMenu(this, [
            Item(Strings.Get(R.Link_New), AppLink.New),
            Item(Strings.Get(R.Link_Today), AppLink.Today),
            Item(Strings.Get(R.Link_Search), AppLink.ForSearch(null)),
        ]);
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

    /// <summary>From the OS creating the process – includes loading the binary, unlike a stopwatch started in Main.</summary>
    private static TimeSpan SinceProcessStart()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return DateTime.Now - process.StartTime;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ready to type {ReadyMs} ms after the process started")]
    private static partial void LogReadyToType(ILogger logger, long readyMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Activation redirected from another launch (with file: {WithFile})")]
    private static partial void LogRedirectedActivation(ILogger logger, bool withFile);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quick note shown")]
    private static partial void LogQuickNoteShown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Link opened: {Kind}")]
    private static partial void LogLinkOpened(ILogger logger, string kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening a link failed")]
    private static partial void LogLinkFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception ({Source})")]
    private static partial void LogUnhandled(ILogger logger, string source, Exception ex);
}
