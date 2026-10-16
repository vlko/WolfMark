using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace WolfMark.App;

/// <summary>
/// Custom entry point (M0-05): the first process owns the window; any later launch redirects its
/// activation to it and exits, so there is always exactly one window.
/// </summary>
public static class Program
{
    private const string InstanceKey = "WolfMark.Main";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToMainInstance())
        {
            return 0;
        }

        Application.Start(static p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    private static bool TryRedirectToMainInstance()
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.Current?.OnRedirectedActivation(e);
            return false;
        }

        RedirectActivation(main, activation);
        return true;
    }

    /// <summary>
    /// Redirects without blocking the STA with Task.Wait: the async call completes on the thread pool and
    /// signals an event that the STA waits on with COM-friendly message pumping.
    /// </summary>
    private static unsafe void RedirectActivation(AppInstance main, AppActivationArguments activation)
    {
        using var redirected = PInvoke.CreateEvent(null, true, false, null);
        var handle = (HANDLE)redirected.DangerousGetHandle();
        _ = main.RedirectActivationToAsync(activation).AsTask().ContinueWith(
            _ => PInvoke.SetEvent(handle),
            TaskScheduler.Default);

        PInvoke.CoWaitForMultipleObjects((uint)CWMO_FLAGS.CWMO_DEFAULT, uint.MaxValue /* INFINITE */, [handle], out _);
    }
}
