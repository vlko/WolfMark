namespace Zapisnik.Index.Indexing;

/// <summary>Raised after index writes. UI refreshes the affected days (calendar) and history.</summary>
public sealed class IndexChangedEventArgs(IReadOnlySet<DateOnly> days, IReadOnlySet<string> paths, bool isFullScan) : EventArgs
{
    public IReadOnlySet<DateOnly> Days { get; } = days;

    public IReadOnlySet<string> Paths { get; } = paths;

    public bool IsFullScan { get; } = isFullScan;
}

public sealed record IndexProgress(string Phase, int Processed, int Total);
