namespace WolfMark.App.Infrastructure;

/// <summary>The UI thread of the hosting framework (WinUI <c>DispatcherQueue</c>, Avalonia <c>Dispatcher</c>).</summary>
internal interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    /// <summary>Queues the action on the UI thread; false when the queue is gone (shutting down).</summary>
    bool TryEnqueue(Action action);
}

/// <summary>Marshals results from background services to the UI thread (CLAUDE.md rule 3).</summary>
internal static class UiThread
{
    private static IUiDispatcher? _dispatcher;

    public static void Initialize(IUiDispatcher dispatcher) => _dispatcher = dispatcher;

    public static bool HasAccess => _dispatcher?.HasThreadAccess == true;

    public static void Post(Action action) => _dispatcher?.TryEnqueue(action);

    public static Task RunAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_dispatcher is null || !_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetCanceled();
        }

        return tcs.Task;
    }
}
