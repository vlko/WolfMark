using System.Globalization;

namespace Zapisnik.Core.Notes;

/// <summary>
/// Location of a note relative to the notes root: <c>YYYY/MM/DD/&lt;name&gt;.md</c>.
/// Always uses '/' as separator (also the key in the index).
/// Files that do not follow the grammar are still notes (<see cref="IsStandard"/> = false).
/// </summary>
public sealed record NotePath
{
    private NotePath(string relativePath, DateOnly? date, NoteName? name)
    {
        RelativePath = relativePath;
        Date = date;
        Name = name;
    }

    /// <summary>Canonical relative path with '/' separators.</summary>
    public string RelativePath { get; }

    /// <summary>Day from the directory structure, if it is a valid YYYY/MM/DD directory.</summary>
    public DateOnly? Date { get; }

    /// <summary>Parsed file name, if it follows the grammar.</summary>
    public NoteName? Name { get; }

    public bool IsStandard => Date is not null && Name is not null;

    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    public string? Tag => Name?.Tag;

    public bool IsConflictCopy => Name?.IsConflictCopy == true;

    /// <summary>Created-at from path + file name; null for non-standard notes (use file creation time).</summary>
    public DateTime? CreatedAt => IsStandard ? Date!.Value.ToDateTime(Name!.Time) : null;

    public static NotePath Create(DateOnly date, NoteName name) =>
        new(FormatDirectory(date) + "/" + name.FileName, date, name);

    public NotePath WithName(NoteName name) =>
        Date is { } d ? Create(d, name) : throw new InvalidOperationException("Non-standard note has no date directory.");

    /// <summary>Path of the original note for a OneDrive conflict copy.</summary>
    public NotePath? ConflictOriginal => IsStandard && Name!.IsConflictCopy ? Create(Date!.Value, Name.WithoutConflict()) : null;

    public static string FormatDirectory(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Year:D4}/{date.Month:D2}/{date.Day:D2}");

    /// <summary>Directory relative path, e.g. "2026/10/15".</summary>
    public string Directory
    {
        get
        {
            var idx = RelativePath.LastIndexOf('/');
            return idx < 0 ? string.Empty : RelativePath[..idx];
        }
    }

    /// <summary>
    /// Parses a path relative to the root (either separator). Returns false only for paths that
    /// are not markdown notes at all (wrong extension, temp files, hidden or internal folders).
    /// </summary>
    public static bool TryParse(string relativePath, out NotePath path)
    {
        path = null!;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var rel = relativePath.Replace('\\', '/').Trim('/');
        var segments = rel.Split('/');
        var fileName = segments[^1];

        if (!IsCandidateFileName(fileName))
        {
            return false;
        }

        foreach (var segment in segments.AsSpan(0, segments.Length - 1))
        {
            if (segment.Length == 0 || segment[0] == '.' || segment.Equals("assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        DateOnly? date = null;
        if (segments.Length == 4 && TryParseDateDirectory(segments[0], segments[1], segments[2], out var d))
        {
            date = d;
        }

        NoteName? name = null;
        if (date is not null && NoteName.TryParse(fileName, out var parsed))
        {
            name = parsed;
        }

        path = new NotePath(rel, date, name);
        return true;
    }

    public static NotePath Parse(string relativePath) =>
        TryParse(relativePath, out var p) ? p : throw new FormatException($"'{relativePath}' is not a note path.");

    /// <summary>True for *.md files that are not hidden/temp files.</summary>
    public static bool IsCandidateFileName(string fileName) =>
        fileName.Length > NoteName.Extension.Length
        && fileName[0] != '.'
        && fileName[0] != '~'
        && fileName.EndsWith(NoteName.Extension, StringComparison.OrdinalIgnoreCase);

    public static bool TryParseDateDirectory(string year, string month, string day, out DateOnly date)
    {
        date = default;
        if (year.Length != 4 || month.Length != 2 || day.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dd))
        {
            return false;
        }

        if (y < 1900 || y > 9999 || m is < 1 or > 12 || dd < 1 || dd > DateTime.DaysInMonth(y, m))
        {
            return false;
        }

        date = new DateOnly(y, m, dd);
        return true;
    }

    public override string ToString() => RelativePath;
}
