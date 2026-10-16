using Microsoft.UI.Dispatching;

namespace WolfMark.App.Infrastructure;

/// <summary>Marshals results from background services to the UI thread (CLAUDE.md rule 3).</summary>
internal static class UiThread
{
    private static DispatcherQueue? _queue;

    public static void Initialize(DispatcherQueue queue) => _queue = queue;

    public static bool HasAccess => _queue?.HasThreadAccess == true;

    public static void Post(Action action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        if (_queue is null)
        {
            return;
        }

        _queue.TryEnqueue(priority, () => action());
    }

    public static Task RunAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_queue is null || !_queue.TryEnqueue(async () =>
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
