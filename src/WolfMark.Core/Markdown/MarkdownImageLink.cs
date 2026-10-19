namespace WolfMark.Core.Markdown;

/// <summary>Image links (<c>![alt](url "title")</c>): parsing and resolving to a local file.</summary>
public static class MarkdownImageLink
{
    /// <summary>Image file types WolfMark previews and accepts by drag &amp; drop.</summary>
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    /// <summary>Splits an <see cref="MarkdownTokenKind.Image"/> token into alt text and URL (title dropped).</summary>
    public static bool TryParse(ReadOnlySpan<char> token, out string alt, out string url)
    {
        alt = url = string.Empty;
        if (!token.StartsWith("![") || token[^1] != ')')
        {
            return false;
        }

        var mid = token.IndexOf("](");
        if (mid < 1)
        {
            return false;
        }

        alt = token[2..mid].ToString();
        var target = token[(mid + 2)..^1].Trim();
        if (target.Length > 1 && target[0] == '<' && target[^1] == '>')
        {
            target = target[1..^1];
        }
        else if (target.IndexOf(' ') is var space and > 0)
        {
            target = target[..space]; // ![a](b.png "title")
        }

        url = target.ToString();
        return true;
    }

    /// <summary>
    /// Resolves a relative image URL against the note's folder. Only relative paths that stay inside the notes
    /// root are accepted: no scheme, no rooted, drive or UNC path – a synced or imported note must not make the
    /// app read <c>\\host\share</c> (credential leak) or reach the network just by being opened.
    /// </summary>
    public static bool TryResolveLocal(string noteDirectory, string notesRoot, string url, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url.Contains(':') || url[0] is '/' or '\\')
        {
            return false;
        }

        string relative;
        try
        {
            relative = Uri.UnescapeDataString(url);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (relative.Length == 0 || relative.Contains(':') || relative[0] is '/' or '\\' || relative.Contains('\0'))
        {
            return false;
        }

        relative = relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(notesRoot)) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(noteDirectory, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
