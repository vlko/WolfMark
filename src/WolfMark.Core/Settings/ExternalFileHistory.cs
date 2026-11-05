using WolfMark.Core.Notes;

namespace WolfMark.Core.Settings;

/// <summary>The list of external files in <see cref="LocalSettings.ExternalFiles"/>: newest first, each file once.</summary>
public static class ExternalFileHistory
{
    public const int Limit = 50;

    /// <summary>Puts <paramref name="path"/> on top with the time it was opened or saved; the oldest fall off past <see cref="Limit"/>.</summary>
    public static void Touch(List<ExternalFileEntry> entries, string path, DateTime utc)
    {
        Remove(entries, path);
        entries.Insert(0, new ExternalFileEntry { Path = path, TouchedUtc = utc });
        if (entries.Count > Limit)
        {
            entries.RemoveRange(Limit, entries.Count - Limit);
        }
    }

    public static bool Remove(List<ExternalFileEntry> entries, string path) =>
        entries.RemoveAll(e => string.Equals(e.Path, path, NotesTree.PathComparison)) > 0;
}
