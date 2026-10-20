using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using Microsoft.Windows.AppLifecycle;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.ViewModels;
using WolfMark.App.Views;

namespace WolfMark.App;

/// <summary>
/// XAML-less WinUI application (docs/adr/0002-ui-in-code.md). Implements <see cref="IXamlMetadataProvider"/>
/// by delegating to the WinUI controls provider, which the XAML compiler would otherwise generate.
/// </summary>
public sealed partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private MainWindow? _window;
    private ILogger? _logger;
    private static DispatcherQueueTimer? _stallTimer; // --trace-editor; kept alive here

    public App()
    {
        Current = this;
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(
            e.ExceptionObject as Exception,
            "AppDomain",
            $"terminating: {e.IsTerminating}, object: {e.ExceptionObject?.GetType().FullName ?? "null"}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal(e.Exception, "TaskScheduler");
            e.SetObserved();
        };
    }

    public static new App? Current { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());

        // Flat design (ADR 0004): controls only slightly rounded, like the tags.
        Resources["ControlCornerRadius"] = new CornerRadius(3);
        Resources["OverlayCornerRadius"] = new CornerRadius(4);
        UiThread.Initialize(new WinUiDispatcher(DispatcherQueue.GetForCurrentThread()));

        try
        {
            var host = await AppHost.CreateAsync(LaunchOptions.Current.DemoDirectory);
            _logger = host.Loggers.CreateLogger<App>();
            if (LaunchOptions.Current.TraceEditor)
            {
                StartEditorTrace(host.Paths.LogsDirectory);
            }

            if (host.Settings.Current.Language is { } language)
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }

            var brushes = new TagBrushes(host.Tags);
            var vm = new MainViewModel(host);
            _window = new MainWindow(vm, brushes);
            WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("startup: window built");
            if (!(StartupRegistration.LaunchedAtStartup && host.Settings.Current.CloseToTray))
            {
                _window.Activate();
                // The shell (title bar, calendar, empty history) is drawn before the note loads – loading a long
                // note keeps the UI thread busy, and until then the window stayed blank.
                await NextFrameAsync();
                WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("startup: first frame");
            }

            // else: started by Windows at sign-in with the tray on – only the tray icon, the window opens from it.

            host.StartBackground();
            await _window.InitializeAsync();
            if (LaunchOptions.Current.ScreenshotDirectory is { } shots)
            {
                await new ScreenshotRunner(_window, host, shots).RunAsync();
            }
        }
        catch (Exception ex)
        {
            // Before the window exists nothing would show the error and the windowless process would keep
            // swallowing every later launch (single instance) – report it and end.
            LogFatal(ex, "OnLaunched");
            if (_window is null || LaunchOptions.Current.ScreenshotDirectory is not null)
            {
                ReportStartupFailure(ex);
                Exit();
            }
        }
    }

    /// <summary>
    /// <c>--trace-editor</c>: the editor records its timings; every 5 s the cumulative table is written to
    /// <c>logs/editor-trace.txt</c> on a background thread.
    /// </summary>
    private static void StartEditorTrace(string logsDirectory)
    {
        var trace = new WolfMark.Core.Diagnostics.EditorTrace();
        WolfMark.Core.Diagnostics.EditorTrace.Current = trace;
        StartStallWatch(trace);
        var file = Path.Combine(logsDirectory, "editor-trace.txt");
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync())
            {
                try
                {
                    Directory.CreateDirectory(logsDirectory);
                    await File.WriteAllTextAsync(file, trace.Report());
                }
                catch (IOException)
                {
                    // next tick
                }
            }
        });
    }

    /// <summary>A 20 ms heartbeat on the UI thread: a late tick is a stall, listed with the marks made meanwhile.</summary>
    private static void StartStallWatch(WolfMark.Core.Diagnostics.EditorTrace trace)
    {
        var interval = TimeSpan.FromMilliseconds(20);
        var timer = _stallTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = interval;
        var last = System.Diagnostics.Stopwatch.GetTimestamp();
        timer.Tick += (_, _) =>
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var late = System.Diagnostics.Stopwatch.GetElapsedTime(last, now) - interval;
            if (late.TotalMilliseconds >= 50)
            {
                var marks = trace.MarksSince(last);
                trace.Record(
                    WolfMark.Core.Diagnostics.EditorPhase.UiStall,
                    (long)(late.TotalSeconds * System.Diagnostics.Stopwatch.Frequency),
                    marks.Count,
                    () => marks.Count == 0 ? "(no marks – editor/RichEdit or layout)" : string.Join(", ", marks));
            }

            last = now;
        };
        timer.Start();
    }

    /// <summary>The next rendered frame, or 500 ms – a hidden or minimized window may not render.</summary>
    private static Task NextFrameAsync()
    {
        var drawn = new TaskCompletionSource();
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            drawn.TrySetResult();
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        return drawn.Task.WaitAsync(TimeSpan.FromMilliseconds(500)).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    internal void OnRedirectedActivation(AppActivationArguments e) => UiThread.Post(() => _window?.BringToFront());

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception, "XAML", e.Message);
        e.Handled = true; // keep the app (and the user's text) alive; the error is in the log
    }

    private static void ReportStartupFailure(Exception ex)
    {
        var text = $"WolfMark could not start.\n\n{ex.GetType().Name}: {ex.Message}";
        try
        {
            var dir = LaunchOptions.Current.DemoDirectory ?? WolfMark.Storage.Settings.LocalPaths.Default.BaseDirectory;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "startup-error.txt"), ex.ToString());
            text += $"\n\nDetails: {Path.Combine(dir, "startup-error.txt")}";
        }
        catch (Exception)
        {
        }

        Windows.Win32.PInvoke.MessageBox(default, text, "WolfMark", Windows.Win32.UI.WindowsAndMessaging.MESSAGEBOX_STYLE.MB_ICONERROR);
    }

    /// <param name="source">Which handler saw it.</param>
    /// <param name="detail">What the handler knows besides the exception – the only information when there is none
    /// (a non-Exception object thrown from native code, or a XAML error with just a message).</param>
    private void LogFatal(Exception? ex, string source, string? detail = null)
    {
        WriteCrashFile(ex, source, detail);
        if (_logger is null)
        {
            return;
        }

        if (ex is not null)
        {
            LogUnhandled(_logger, source, detail ?? string.Empty, ex);
        }
        else
        {
            LogUnhandledWithoutException(_logger, source, detail ?? "no details");
        }
    }

    /// <summary>
    /// Synchronous copy of every unhandled exception in <c>logs\crash.txt</c>: the process may end right after
    /// (AppDomain.UnhandledException), before the logger gets to write.
    /// </summary>
    private static void WriteCrashFile(Exception? ex, string source, string? detail)
    {
        try
        {
            var dir = LaunchOptions.Current.DemoDirectory is { } demo
                ? Path.Combine(demo, "local", "logs")
                : WolfMark.Storage.Settings.LocalPaths.Default.LogsDirectory;
            Directory.CreateDirectory(dir);
            var what = ex?.ToString() ?? "(no exception object)";
            var extra = detail is null ? string.Empty : $" ({detail})";
            File.AppendAllText(
                Path.Combine(dir, "crash.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}]{extra} {what}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // nothing left to report to
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception ({Source}) {Detail}")]
    private static partial void LogUnhandled(ILogger logger, string source, string detail, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled error without an exception object ({Source}): {Detail}")]
    private static partial void LogUnhandledWithoutException(ILogger logger, string source, string detail);

    public IXamlType GetXamlType(System.Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
