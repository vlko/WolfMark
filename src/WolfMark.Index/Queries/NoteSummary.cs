namespace WolfMark.Index.Queries;

/// <summary>A note as listed in history and search results.</summary>
public sealed record NoteSummary(
    long Id,
    string RelativePath,
    DateTime CreatedAt,
    DateOnly Day,
    string? PrimaryTag,
    string Title,
    string Preview,
    int WordCount,
    int OpenTasks,
    int DoneTasks,
    string Source,
    bool IsStandard,
    bool IsConflict,
    bool IsPlaceholder)
{
    public bool IsClaudeCode => Source == NoteSources.ClaudeCode;
}

public static class NoteSources
{
    public const string User = "user";
    public const string ClaudeCode = "claudecode";
}
