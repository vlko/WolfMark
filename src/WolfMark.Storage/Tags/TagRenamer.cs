using WolfMark.Core.Notes;
using WolfMark.Core.Tags;

namespace WolfMark.Storage.Tags;

/// <summary>
/// Renames a tag across all notes (M8-05): every file named <c>old_HHmm.md</c> becomes <c>new_HHmm.md</c>. Renaming to
/// a tag that already exists merges the two. Only file names change – <c>#tag</c> in the text stays as written. A
/// failure renames the files already done back, so the notes end up as they were.
/// </summary>
public static class TagRenamer
{
    /// <summary>The result of <see cref="RenameAsync"/>.</summary>
    /// <param name="Renamed">Notes that carry the new tag now.</param>
    /// <param name="Error">The rename that failed (everything was renamed back), or null.</param>
    /// <param name="RollbackFailures">Notes that could not be renamed back after the failure (they keep the new tag).</param>
    public sealed record Result(int Renamed, Exception? Error, IReadOnlyList<NotePath> RollbackFailures);

    /// <summary>Why a rename is not possible, or null when it is.</summary>
    public enum Problem
    {
        InvalidName,
        SameName,
        Reserved,
    }

    /// <summary>
    /// Checks the new name: a valid tag, not the same, not the reserved <see cref="TagName.ClaudeCode"/> (those notes
    /// belong to the sync). A change of letter case only is allowed.
    /// </summary>
    public static Problem? Validate(string oldTag, string? newTag, out string normalized)
    {
        normalized = string.Empty;
        if (!TagName.TryNormalize(newTag, out normalized))
        {
            return Problem.InvalidName;
        }

        if (string.Equals(oldTag, normalized, StringComparison.Ordinal))
        {
            return Problem.SameName;
        }

        return TagName.Comparer.Equals(oldTag, TagName.ClaudeCode) || TagName.Comparer.Equals(normalized, TagName.ClaudeCode)
            ? Problem.Reserved
            : null;
    }

    /// <summary>Notes whose file name carries <paramref name="tag"/> (any letter case), read from the files.</summary>
    public static async Task<IReadOnlyList<NotePath>> FindAsync(DirectoryEngine engine, string tag, CancellationToken ct)
    {
        var key = TagName.ToKey(tag);
        var found = new List<NotePath>();
        await foreach (var file in engine.EnumerateAsync(ct).ConfigureAwait(false))
        {
            if (file.Path.IsStandard && file.Path.Tag is { } t && string.Equals(TagName.ToKey(t), key, StringComparison.Ordinal))
            {
                found.Add(file.Path);
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return found;
    }

    /// <summary>
    /// Renames <paramref name="notes"/> to <paramref name="newTag"/> one by one with <paramref name="changeTag"/>
    /// (<see cref="DirectoryEngine.ChangeTagAsync"/>). A started rename is not cancellable: it completes or rolls back.
    /// </summary>
    public static async Task<Result> RenameAsync(
        IReadOnlyList<NotePath> notes,
        string newTag,
        Func<NotePath, string?, Task<NotePath>> changeTag,
        IProgress<int>? progress)
    {
        var done = new List<(NotePath Original, NotePath Renamed)>(notes.Count);
        foreach (var note in notes)
        {
            try
            {
                var renamed = await changeTag(note, newTag).ConfigureAwait(false);
                done.Add((note, renamed));
                progress?.Report(done.Count);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var failures = new List<NotePath>();
                for (var i = done.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        await changeTag(done[i].Renamed, done[i].Original.Tag).ConfigureAwait(false);
                    }
                    catch (Exception back) when (back is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        failures.Add(done[i].Renamed);
                    }
                }

                return new Result(failures.Count, ex, failures);
            }
        }

        return new Result(done.Count, null, []);
    }
}
