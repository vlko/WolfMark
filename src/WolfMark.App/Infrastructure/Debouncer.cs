namespace WolfMark.App.Infrastructure;

/// <summary>Runs the last scheduled action after a quiet period; cancels superseded runs.</summary>
internal sealed class Debouncer(TimeSpan delay) : IDisposable
{
    private CancellationTokenSource? _cts;

    public void Schedule(Func<CancellationToken, Task> action)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _cts, cts)?.Cancel();
        _ = RunAsync(action, cts.Token);
    }

    public void Cancel() => Interlocked.Exchange(ref _cts, null)?.Cancel();

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(true);
            await action(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => Cancel();
}
