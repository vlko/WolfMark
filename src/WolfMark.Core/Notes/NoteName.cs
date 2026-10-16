using System.Globalization;
using System.Text;
using WolfMark.Core.Tags;

namespace WolfMark.Core.Notes;

/// <summary>
/// File name of a note (SPEC §3.3):
/// <code>
/// &lt;tag&gt;_&lt;HHmm&gt;[ss][-n][-CONFLICT].md
/// &lt;HHmm&gt;[ss][-n][-CONFLICT].md
/// </code>
/// <c>-n</c> is a collision suffix (n ≥ 2); a non-numeric suffix is a OneDrive conflict copy marker
/// (e.g. <c>praca_1403-MYPC.md</c>).
/// </summary>
public sealed record NoteName
{
    public const string Extension = ".md";
    public const char TagSeparator = '_';

    private NoteName(string? tag, TimeOnly time, bool hasSeconds, int suffix, string? conflictMarker)
    {
        Tag = tag;
        Time = time;
        HasSeconds = hasSeconds;
        Suffix = suffix;
        ConflictMarker = conflictMarker;
    }

    /// <summary>Primary tag (NFC, display casing) or null for untagged notes.</summary>
    public string? Tag { get; }

    /// <summary>Creation time of the note (seconds are 0 unless <see cref="HasSeconds"/>).</summary>
    public TimeOnly Time { get; }

    public bool HasSeconds { get; }

    /// <summary>Collision suffix; 0 means none, otherwise ≥ 2.</summary>
    public int Suffix { get; }

    /// <summary>OneDrive conflict copy marker (usually a machine name), or null.</summary>
    public string? ConflictMarker { get; }

    public bool IsConflictCopy => ConflictMarker is not null;

    /// <summary>The file this conflict copy belongs to (same name without the conflict marker).</summary>
    public NoteName WithoutConflict() => ConflictMarker is null ? this : new(Tag, Time, HasSeconds, Suffix, null);

    public static NoteName Create(string? tag, TimeOnly time, bool includeSeconds = false, int suffix = 0)
    {
        string? normalizedTag = null;
        if (tag is not null && !TagName.TryNormalize(tag, out normalizedTag))
        {
            throw new ArgumentException($"Invalid tag '{tag}'.", nameof(tag));
        }

        if (suffix is 1 or < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suffix), "Suffix must be 0 (none) or >= 2.");
        }

        var t = includeSeconds
            ? new TimeOnly(time.Hour, time.Minute, time.Second)
            : new TimeOnly(time.Hour, time.Minute);

        return new NoteName(normalizedTag, t, includeSeconds, suffix, null);
    }

    public NoteName WithTag(string? tag) => Create(tag, Time, HasSeconds, Suffix);

    /// <summary>
    /// Next candidate name when this one is taken: first add seconds, then -2, -3, …
    /// </summary>
    public NoteName NextCollisionCandidate(int secondsIfMissing)
    {
        if (!HasSeconds)
        {
            return new NoteName(Tag, new TimeOnly(Time.Hour, Time.Minute, secondsIfMissing), true, 0, null);
        }

        return new NoteName(Tag, Time, true, Suffix == 0 ? 2 : Suffix + 1, null);
    }

    public string FileName
    {
        get
        {
            var sb = new StringBuilder(32);
            if (Tag is not null)
            {
                sb.Append(Tag).Append(TagSeparator);
            }

            sb.Append(CultureInfo.InvariantCulture, $"{Time.Hour:D2}{Time.Minute:D2}");
            if (HasSeconds)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{Time.Second:D2}");
            }

            if (Suffix >= 2)
            {
                sb.Append('-').Append(Suffix.ToString(CultureInfo.InvariantCulture));
            }

            if (ConflictMarker is not null)
            {
                sb.Append('-').Append(ConflictMarker);
            }

            return sb.Append(Extension).ToString();
        }
    }

    public override string ToString() => FileName;

    public static bool TryParse(string? fileName, out NoteName name)
    {
        name = null!;
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = fileName.AsSpan(0, fileName.Length - Extension.Length);
        if (stem.IsEmpty)
        {
            return false;
        }

        string? tag = null;
        var sep = stem.IndexOf(TagSeparator);
        if (sep >= 0)
        {
            var tagSpan = stem[..sep];
            var tagString = tagSpan.ToString();
            if (!tagString.IsNormalized(NormalizationForm.FormC))
            {
                tagString = tagString.Normalize(NormalizationForm.FormC);
            }

            if (!TagName.IsValid(tagString))
            {
                return false;
            }

            tag = tagString;
            stem = stem[(sep + 1)..];
        }

        // Time: HHmm[ss]
        var digits = 0;
        while (digits < stem.Length && digits < 6 && char.IsAsciiDigit(stem[digits]))
        {
            digits++;
        }

        if (digits is not (4 or 6))
        {
            return false;
        }

        var hour = ParseTwo(stem, 0);
        var minute = ParseTwo(stem, 2);
        var second = digits == 6 ? ParseTwo(stem, 4) : 0;
        if (hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        var rest = stem[digits..];
        var suffix = 0;
        string? conflict = null;

        if (!rest.IsEmpty)
        {
            if (rest[0] != '-' || rest.Length == 1)
            {
                return false;
            }

            rest = rest[1..];
            var dash = rest.IndexOf('-');
            var first = dash >= 0 ? rest[..dash] : rest;

            if (IsCollisionNumber(first))
            {
                suffix = int.Parse(first, NumberStyles.None, CultureInfo.InvariantCulture);
                rest = dash >= 0 ? rest[(dash + 1)..] : [];
                if (dash >= 0 && rest.IsEmpty)
                {
                    return false;
                }
            }

            if (!rest.IsEmpty)
            {
                if (!IsValidConflictMarker(rest))
                {
                    return false;
                }

                conflict = rest.ToString();
            }
        }

        name = new NoteName(tag, new TimeOnly(hour, minute, second), digits == 6, suffix, conflict);
        return true;
    }

    private static int ParseTwo(ReadOnlySpan<char> s, int index) => ((s[index] - '0') * 10) + (s[index + 1] - '0');

    private static bool IsCollisionNumber(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty || s.Length > 4 || s[0] == '0')
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return !(s.Length == 1 && s[0] == '1');
    }

    private static bool IsValidConflictMarker(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty || s.Length > 64)
        {
            return false;
        }

        var hasNonDigit = false;
        foreach (var c in s)
        {
            if (c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '.' || char.IsWhiteSpace(c))
            {
                return false;
            }

            hasNonDigit |= !char.IsAsciiDigit(c);
        }

        return hasNonDigit;
    }
}
