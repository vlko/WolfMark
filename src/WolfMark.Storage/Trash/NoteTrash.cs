using System.Globalization;
using System.Text.RegularExpressions;
using WolfMark.Storage.Settings;

namespace WolfMark.Storage.Trash;

/// <summary>A note in the trash: where it lies, where it was and when it was deleted.</summary>
/// <param name="OriginalRelativePath">Path in the notes tree it had, "2026/11/16/praca_1403.md".</param>
/// <param name="Title">The first line with text, without heading marks; empty for an empty file.</param>
public sealed record TrashedNote(string FullPath, string OriginalRelativePath, DateTime DeletedAtUtc, long Size, string Title);

/// <summary>
/// The trash of the notes on every system (ADR 0016): <c>&lt;root&gt;/.wolfmark/trash</c>, the notes in the folders
/// they had, each named with the time it was deleted (<c>praca_1403.20261117-135357.md</c>). It lies with the notes,
/// so it travels with them (OneDrive, a portable drive) and needs no database – deleting the index loses nothing.
/// Listing and restoring is Settings → Trash; nothing leaves it unless the user empties it or sets a retention.
/// </summary>
public sealed partial class NoteTrash(string root, TimeProvider time) : IRecycleBin
{
    private const string StampFormat = "yyyyMMdd-HHmmss";

    public string TrashDirectory { get; } = Path.Combine(root, DirectoryEngine.InternalDirectoryName, "trash");

    public Task MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(() => Move(fullPath), cancellationToken);

    private void Move(string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        var folder = Path.Combine(TrashDirectory, Path.GetDirectoryName(relative) ?? string.Empty);
        Directory.CreateDirectory(folder);
        var stamp = time.GetUtcNow().UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
        var name = Path.GetFileNameWithoutExtension(relative);
        var extension = Path.GetExtension(relative);
        var isFolder = Directory.Exists(fullPath);
        for (var n = 1; ; n++)
        {
            // Two notes of one name deleted within a second: "-2", "-3", …
            var target = Path.Combine(folder, $"{name}.{stamp}{(n == 1 ? string.Empty : "-" + n.ToString(CultureInfo.InvariantCulture))}{extension}");
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            if (isFolder)
            {
                Directory.Move(fullPath, target);
            }
            else
            {
                File.Move(fullPath, target, overwrite: false);
            }

            return;
        }
    }

    /// <summary>The notes in the trash, the last deleted first. Reads the start of each file for its title.</summary>
    public IReadOnlyList<TrashedNote> List()
    {
        if (!Directory.Exists(TrashDirectory))
        {
            return [];
        }

        var notes = new List<TrashedNote>();
        foreach (var file in Directory.EnumerateFiles(TrashDirectory, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                var folder = Path.GetRelativePath(TrashDirectory, info.DirectoryName!).Replace('\\', '/');
                var (name, deleted) = ParseName(info.Name, info.LastWriteTimeUtc);
                var original = folder is "." or "" ? name : folder + "/" + name;
                notes.Add(new TrashedNote(file, original, deleted, info.Length, ReadTitle(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Vanished or locked meanwhile (a sync client at work): not listed this time.
            }
        }

        notes.Sort((a, b) => b.DeletedAtUtc.CompareTo(a.DeletedAtUtc));
        return notes;
    }

    /// <summary>
    /// Removes a note from the trash: into <paramref name="systemBin"/> where the system has one (so even this can be
    /// undone there), otherwise for good – the one place the app deletes, and only when the user asks for it.
    /// </summary>
    public async Task DeleteAsync(TrashedNote note, IRecycleBin? systemBin, CancellationToken cancellationToken)
    {
        if (!IsInTrash(note.FullPath) || !File.Exists(note.FullPath))
        {
            return;
        }

        if (systemBin is not null)
        {
            await systemBin.MoveToRecycleBinAsync(note.FullPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => File.Delete(note.FullPath), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Removes the notes deleted before <paramref name="deletedBeforeUtc"/> (null = all of them).</summary>
    /// <returns>How many were removed.</returns>
    public async Task<int> EmptyAsync(DateTime? deletedBeforeUtc, IRecycleBin? systemBin, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var note in await Task.Run(List, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deletedBeforeUtc is { } limit && note.DeletedAtUtc >= limit)
            {
                continue;
            }

            await DeleteAsync(note, systemBin, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        await Task.Run(RemoveEmptyFolders, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    /// <summary>After a restore or an emptying: day folders left without a note go (deepest first).</summary>
    public void RemoveEmptyFolders()
    {
        if (!Directory.Exists(TrashDirectory))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(TrashDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private bool IsInTrash(string fullPath) => PathScope.IsSameOrUnder(fullPath, TrashDirectory);

    /// <summary>
    /// "praca_1403.20261117-135357.md" → the name it had and when it was deleted. Files put there by older versions
    /// have no time in the name (or a 17-digit one after a collision): their last change stands in for it.
    /// </summary>
    internal static (string Name, DateTime DeletedAtUtc) ParseName(string fileName, DateTime fallbackUtc)
    {
        var match = StampedName().Match(fileName);
        if (match.Success && DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var deleted))
        {
            return (match.Groups["name"].Value + match.Groups["ext"].Value, deleted);
        }

        var legacy = LegacyCollisionName().Match(fileName);
        return (legacy.Success ? legacy.Groups["name"].Value + legacy.Groups["ext"].Value : fileName, fallbackUtc);
    }

    private static string ReadTitle(string file)
    {
        var buffer = new byte[2048];
        int read;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }

        var text = DirectoryEngine.Decode(buffer.AsSpan(0, read));
        var inFrontMatter = false;
        var first = true;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (first && line == "---")
            {
                inFrontMatter = true; // Claude Code notes start with front matter – the title follows it
                first = false;
                continue;
            }

            first = false;
            if (inFrontMatter)
            {
                inFrontMatter = line is not ("---" or "...");
                continue;
            }

            var title = line.TrimStart('#', ' ', '\t', '>', '-', '*').Trim();
            if (title.Length > 0)
            {
                return title.Length > 120 ? title[..120] + "…" : title;
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"^(?<name>.+)\.(?<stamp>\d{8}-\d{6})(-\d+)?(?<ext>\.[^.]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex StampedName();

    [GeneratedRegex(@"^(?<name>.+)\.\d{17}(?<ext>\.[^.]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyCollisionName();
}
