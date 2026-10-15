using System.Collections.Concurrent;

namespace Zapisnik.Storage;

/// <summary>
/// Remembers what this process wrote so file-watcher echoes of our own saves do not reload the editor
/// (SPEC §4.1). Content echoes are matched by hash (always safe: same content = nothing to reload);
/// renames/deletes by path within a short window.
/// </summary>
public sealed class SelfWriteRegistry(TimeProvider timeProvider)
{
    private static readonly TimeSpan ExpectationWindow = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, string> _lastWrittenHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expectedRemovals = new(StringComparer.OrdinalIgnoreCase);

    public SelfWriteRegistry()
        : this(TimeProvider.System)
    {
    }

    public void RecordWrite(string relativePath, string contentHash) => _lastWrittenHash[relativePath] = contentHash;

    public bool IsOwnContent(string relativePath, string contentHash) =>
        _lastWrittenHash.TryGetValue(relativePath, out var h) && h == contentHash;

    public void Forget(string relativePath) => _lastWrittenHash.TryRemove(relativePath, out _);

    /// <summary>We are about to delete/rename this path; the resulting watcher event is ours.</summary>
    public void ExpectRemoval(string relativePath) => _expectedRemovals[relativePath] = timeProvider.GetUtcNow();

    public bool ConsumeExpectedRemoval(string relativePath)
    {
        if (_expectedRemovals.TryRemove(relativePath, out var at))
        {
            return timeProvider.GetUtcNow() - at < ExpectationWindow;
        }

        return false;
    }
}
