using System.Globalization;
using WolfMark.Core.Notes;

namespace WolfMark.Core.Links;

public enum AppLinkKind
{
    /// <summary><c>wolfmark://today</c> – today, all notes in the history.</summary>
    Today,

    /// <summary><c>wolfmark://day/2026-11-16</c>.</summary>
    Day,

    /// <summary><c>wolfmark://note/2026/11/16/praca_1403</c> – the note's path without <c>.md</c>.</summary>
    Note,

    /// <summary><c>wolfmark://new</c> – the quick note window (M8-01).</summary>
    New,

    /// <summary><c>wolfmark://search?q=…</c> – the search, optionally with a query.</summary>
    Search,
}

/// <summary>
/// A <c>wolfmark://</c> link (M8-03, ADR 0013): from other apps through the registered protocol, the jump list and
/// the command line. Links only navigate – nothing is written or deleted – because any web page can open one; a note
/// path has to stay a note path inside the notes tree (no <c>..</c>, no hidden or internal folders).
/// </summary>
public sealed record AppLink
{
    public const string Scheme = "wolfmark";

    private AppLink(AppLinkKind kind, DateOnly? day = null, NotePath? note = null, string? query = null)
    {
        Kind = kind;
        Day = day;
        Note = note;
        Query = query;
    }

    public AppLinkKind Kind { get; }

    public DateOnly? Day { get; }

    public NotePath? Note { get; }

    /// <summary>The search text of <see cref="AppLinkKind.Search"/>, null for an empty search.</summary>
    public string? Query { get; }

    public static AppLink Today { get; } = new(AppLinkKind.Today);

    public static AppLink New { get; } = new(AppLinkKind.New);

    public static AppLink ForDay(DateOnly day) => new(AppLinkKind.Day, day: day);

    public static AppLink ForNote(NotePath note) => new(AppLinkKind.Note, note: note);

    public static AppLink ForSearch(string? query) => new(AppLinkKind.Search, query: string.IsNullOrWhiteSpace(query) ? null : query);

    /// <summary>True for text that starts with <c>wolfmark:</c> – a link, valid or not (never a file name).</summary>
    public static bool LooksLikeLink(string text) => text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? text, out AppLink link)
    {
        link = Today;
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeLink(text.Trim()))
        {
            return false;
        }

        // wolfmark://host/path?query – parsed by hand: System.Uri lower-cases the host and is lenient with '\'.
        var rest = text.Trim()[(Scheme.Length + 1)..];
        if (!rest.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest[2..];
        string? query = null;
        var q = rest.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            query = rest[(q + 1)..];
            rest = rest[..q];
        }

        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            rest = rest[..hash];
        }

        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var target = segments[0].ToLowerInvariant();
        var args = segments.AsSpan(1);
        switch (target)
        {
            case "today" when args.IsEmpty:
                link = Today;
                return true;
            case "new" when args.IsEmpty:
                link = New;
                return true;
            case "search" when args.IsEmpty:
                link = ForSearch(QueryValue(query, "q"));
                return true;
            case "day" when args.Length == 1
                && DateOnly.TryParseExact(args[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day):
                link = ForDay(day);
                return true;
            case "note" when !args.IsEmpty && TryNotePath(args, out var note):
                link = ForNote(note);
                return true;
            default:
                return false;
        }
    }

    public override string ToString() => Kind switch
    {
        AppLinkKind.Today => Scheme + "://today",
        AppLinkKind.New => Scheme + "://new",
        AppLinkKind.Day => Scheme + "://day/" + Day!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        AppLinkKind.Search => Scheme + "://search" + (Query is null ? string.Empty : "?q=" + Uri.EscapeDataString(Query)),
        _ => Scheme + "://note/" + string.Join('/', WithoutExtension(Note!.RelativePath).Split('/').Select(Uri.EscapeDataString)),
    };

    private static string WithoutExtension(string path) =>
        path.EndsWith(NoteName.Extension, StringComparison.OrdinalIgnoreCase) ? path[..^NoteName.Extension.Length] : path;

    private static bool TryNotePath(ReadOnlySpan<string> segments, out NotePath note)
    {
        note = null!;
        var parts = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            string part;
            try
            {
                part = Uri.UnescapeDataString(segments[i]);
            }
            catch (UriFormatException)
            {
                return false;
            }

            // An escaped separator or a relative step must not leave the notes tree.
            if (part.Length == 0 || part is "." or ".." || part.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
            {
                return false;
            }

            parts[i] = part;
        }

        var path = string.Join('/', parts);
        if (!path.EndsWith(NoteName.Extension, StringComparison.OrdinalIgnoreCase))
        {
            path += NoteName.Extension;
        }

        return NotePath.TryParse(path, out note);
    }

    /// <summary>The value of <paramref name="name"/> in a query string (<c>q=a+b%20c</c>), null when missing or empty.</summary>
    private static string? QueryValue(string? query, string name)
    {
        if (query is null)
        {
            return null;
        }

        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var key = eq < 0 ? pair : pair[..eq];
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = eq < 0 ? string.Empty : pair[(eq + 1)..].Replace('+', ' ');
            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
