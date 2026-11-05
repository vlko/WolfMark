namespace WolfMark.Core.Notes;

/// <summary>Where a file on disk stands relative to the notes root.</summary>
public static class NotesTree
{
    /// <summary>Path comparison of the platform's file system: case-insensitive on Windows only.</summary>
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>True when <paramref name="fullPath"/> lies under <paramref name="root"/> (not the root itself).</summary>
    public static bool Contains(string root, string fullPath)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(fullPath).StartsWith(prefix, PathComparison);
    }

    /// <summary>
    /// The note for a file opened from outside (file association, command line): a <c>.md</c> file under the root
    /// that the notes tree indexes. Files in hidden or <c>assets</c> folders are not notes – they open as external files.
    /// </summary>
    public static bool TryGetNotePath(string root, string fullPath, out NotePath path)
    {
        path = null!;
        if (!Contains(root, fullPath))
        {
            return false;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(fullPath));
        return NotePath.TryParse(relative, out path);
    }
}
