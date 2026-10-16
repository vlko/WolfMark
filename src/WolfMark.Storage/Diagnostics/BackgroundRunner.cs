using Microsoft.Extensions.Logging;

namespace WolfMark.Storage.Diagnostics;

/// <summary>
/// Starts fire-and-forget background work whose exceptions are logged instead of crashing the app
/// (M0-03). Cancellation is not an error.
/// </summary>
public sealed partial class BackgroundRunner(ILogger<BackgroundRunner> logger)
{
    public Task Run(string name, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                try
                {
                    await work(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LogFailed(ex, name);
                }
            },
            CancellationToken.None);

    [LoggerMessage(Level = LogLevel.Error, Message = "Background task {Name} failed")]
    private partial void LogFailed(Exception ex, string name);
}
