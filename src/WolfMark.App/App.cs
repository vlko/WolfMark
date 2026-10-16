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

    public App()
    {
        Current = this;
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal(e.Exception);
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
        UiThread.Initialize(DispatcherQueue.GetForCurrentThread());

        var host = await AppHost.CreateAsync();
        _logger = host.Loggers.CreateLogger<App>();
        if (host.Settings.Current.Language is { } language)
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }

        var brushes = new TagBrushes(host.Tags);
        var vm = new MainViewModel(host);
        _window = new MainWindow(vm, brushes);
        if (!(StartupRegistration.LaunchedAtStartup && host.Settings.Current.CloseToTray))
        {
            _window.Activate();
        }

        // else: started by Windows at sign-in with the tray on – only the tray icon, the window opens from it.

        host.StartBackground();
        await _window.InitializeAsync();
    }

    internal void OnRedirectedActivation(AppActivationArguments e) => UiThread.Post(() => _window?.BringToFront());

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        e.Handled = true; // keep the app (and the user's text) alive; the error is in the log
    }

    private void LogFatal(Exception? ex)
    {
        if (ex is not null && _logger is not null)
        {
            LogUnhandled(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception ex);

    public IXamlType GetXamlType(System.Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
