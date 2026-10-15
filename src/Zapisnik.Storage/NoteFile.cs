using Zapisnik.Core.Notes;

namespace Zapisnik.Storage;

/// <summary>File metadata from enumeration – never requires reading (or hydrating) the content.</summary>
public sealed record NoteFile(
    NotePath Path,
    string FullPath,
    DateTimeOffset LastWriteTimeUtc,
    DateTimeOffset CreationTimeUtc,
    long Length,
    bool IsCloudPlaceholder)
{
    /// <summary>mtime as stored in the index.</summary>
    public long MtimeTicks => LastWriteTimeUtc.UtcTicks;

    /// <summary>Created-at from the path, or the local file creation time for non-standard names.</summary>
    public DateTime CreatedAtLocal => Path.CreatedAt ?? CreationTimeUtc.ToLocalTime().DateTime;

    public DateOnly Day => Path.Date ?? DateOnly.FromDateTime(CreatedAtLocal);
}
