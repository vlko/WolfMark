using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using Microsoft.Windows.AppLifecycle;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Diagnostics;
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
        StartupTimeline.Mark("Windows App SDK, XAML start");
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        StartupTimeline.Mark("XamlControlsResources");

        // Flat design (ADR 0004): controls only slightly rounded, like the tags.
        Resources["ControlCornerRadius"] = new CornerRadius(3);
        Resources["OverlayCornerRadius"] = new CornerRadius(4);
        UiThread.Initialize(new WinUiDispatcher(DispatcherQueue.GetForCurrentThread()));

        try
        {
            var launched = StartupTimeline.Now();
            StartupTimeline.Mark("OnLaunched");
            var host = await AppHost.CreateAsync(LaunchOptions.Current.DemoDirectory, freshDemo: LaunchOptions.Current.ScreenshotDirectory is not null);
            var hostReady = StartupTimeline.Now();
            StartupTimeline.Mark("data and index open");
            var logger = host.Loggers.CreateLogger<App>();
            _logger = logger;
            if (LaunchOptions.Current.TraceEditor)
            {
                StartEditorTrace(host.Paths.LogsDirectory);
            }

            if (host.Settings.Current.Language is { } language)
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }

            // Day/month names and the time format: the language's or the region's (Settings), as in the Avalonia app.
            UiCulture.Apply(host.Settings.Current.Language, host.Settings.Current.DateFormatsFromLanguage);

            var brushes = new TagBrushes(host.Tags);
            long? firstFrame = null;
            StartupTimeline.Mark("App: settings, brushes");
            WolfMark.Editor.MarkdownEditor.DeferInitialLayout = StartupExperiment.Is("editor-defer-layout");
            var vm = new MainViewModel(host);
            StartupTimeline.Mark("view model");
            _window = new MainWindow(vm, brushes);
            var windowBuilt = StartupTimeline.Now();
            WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("startup: window built");
            if (!(StartupRegistration.LaunchedAtStartup && host.Settings.Current.CloseToTray))
            {
                _window.Activate();
                var activated = StartupTimeline.Now();
                StartupTimeline.Mark("Activate");
                // The shell (title bar, calendar, empty history) is drawn before the note loads – loading a long
                // note keeps the UI thread busy, and until then the window stayed blank.
                await NextFrameAsync();
                WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("startup: first frame");
                firstFrame = StartupTimeline.Now();
                StartupTimeline.Mark("first frame");
                LogStartupPhases(logger, StartupTimeline.MainMs, launched, hostReady, windowBuilt, activated, firstFrame.Value);
            }

            // else: started by Windows at sign-in with the tray on – only the tray icon, the window opens from it.

            host.StartBackground();
            await _window.InitializeAsync(() =>
            {
                if (firstFrame is { } shown)
                {
                    // M4-09 (start to typing): the start note is in the editor and it has the focus.
                    LogReadyToType(logger, StartupTimeline.Now(), shown);
                }
            });
            if (firstFrame is not null)
            {
                StartupTimeline.Mark("panels loaded (week, history, calendar, counts)");
                LogStartupMarks(logger, StartupTimeline.TakeMarks() + "; experiment " + StartupExperiment.Active);
            }

            if (LaunchOptions.Current.OpenFile is { } file)
            {
                await vm.OpenFileAsync(file);
            }

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
                    () => marks.Count == 0 ? "(no marks – editor, layout or rendering)" : string.Join(", ", marks));
            }

            last = now;
        };
        timer.Start();
    }

    /// <summary>The next rendered frame, or 500 ms – a hidden or minimized window may not render.</summary>
    /// <summary>
    /// The next rendered frame, at most 500 ms. No exception on the timeout: a WaitAsync(…).ContinueWith(_ => …) left
    /// the TimeoutException unobserved, and the finalizer reported it as an unhandled error (smoke test, 2026-11-16).
    /// </summary>
    private static async Task NextFrameAsync()
    {
        var drawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object> handler = (_, _) => drawn.TrySetResult();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        using var expire = timeout.Token.Register(() => drawn.TrySetResult());
        try
        {
            await drawn.Task;
        }
        finally
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler; // back on the UI thread
        }
    }

    /// <summary>
    /// A later start (another command line, or Explorer opening a <c>.md</c> file): its file opens here, otherwise
    /// the window just comes to the front. Runs on a background thread.
    /// </summary>
    internal void OnRedirectedActivation(AppActivationArguments e)
    {
        var file = RedirectedFile(e);
        if (_logger is not null)
        {
            LogRedirectedActivation(_logger, file is not null); // tools/smoke-windows.ps1 checks for it
        }

        UiThread.Post(() =>
        {
            if (_window is null)
            {
                return;
            }

            if (file is null)
            {
                _window.BringToFront();
            }
            else
            {
                _ = _window.ViewModel.OpenFileAsync(file);
            }
        });
    }

    /// <summary>
    /// The file of a redirected activation. A launch carries the second process's command line as one string – with
    /// or without the program in front, so a first word that is not an .exe is taken as an argument already.
    /// </summary>
    private static string? RedirectedFile(AppActivationArguments e)
    {
        switch (e.Data)
        {
            case Windows.ApplicationModel.Activation.IFileActivatedEventArgs files:
                return files.Files.Count > 0 ? files.Files[0].Path : null;
            case Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch when !string.IsNullOrWhiteSpace(launch.Arguments):
                var args = CommandLine.Split(launch.Arguments);
                if (args.Count > 0 && !args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    args.Insert(0, "WolfMark.exe");
                }

                return LaunchOptions.Parse(args).OpenFile;
            default:
                return null;
        }
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup marks (ms since the previous one): {Marks}")]
    private static partial void LogStartupMarks(ILogger logger, string marks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup (ms since the process started): Main {MainMs}, OnLaunched {LaunchedMs}, data and index open {HostMs}, window built {WindowMs}, activated {ActivatedMs}, first frame {FirstFrameMs}")]
    private static partial void LogStartupPhases(ILogger logger, long mainMs, long launchedMs, long hostMs, long windowMs, long activatedMs, long firstFrameMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ready to type {ReadyMs} ms after the process started (first frame {FirstFrameMs} ms)")]
    private static partial void LogReadyToType(ILogger logger, long readyMs, long firstFrameMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Activation redirected from another launch (with file: {WithFile})")]
    private static partial void LogRedirectedActivation(ILogger logger, bool withFile);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception ({Source}) {Detail}")]
    private static partial void LogUnhandled(ILogger logger, string source, string detail, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled error without an exception object ({Source}): {Detail}")]
    private static partial void LogUnhandledWithoutException(ILogger logger, string source, string detail);

    public IXamlType GetXamlType(System.Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
