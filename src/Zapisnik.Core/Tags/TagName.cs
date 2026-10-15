using System.Globalization;
using System.Text;

namespace Zapisnik.Core.Tags;

/// <summary>
/// Tag rules from SPEC §3.3: letters (including diacritics), digits and '-'. NFC normalised,
/// compared case-insensitively, displayed as first seen.
/// </summary>
public static class TagName
{
    public const int MaxLength = 64;

    /// <summary>Reserved tag used by the Claude Code import.</summary>
    public const string ClaudeCode = "ClaudeCode";

    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static bool IsValid(string? tag) => tag is not null && IsValid(tag.AsSpan());

    public static bool IsValid(ReadOnlySpan<char> tag)
    {
        if (tag.IsEmpty || tag.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in tag)
        {
            if (!IsAllowedChar(c))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedChar(char c)
    {
        if (c == '-')
        {
            return true;
        }

        if (char.IsLetterOrDigit(c))
        {
            return true;
        }

        // Combining marks can survive NFC for some scripts; they are part of a letter.
        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    /// <summary>Normalises to NFC and validates. Returns false for invalid tags.</summary>
    public static bool TryNormalize(string? input, out string tag)
    {
        tag = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        var normalized = trimmed.IsNormalized(NormalizationForm.FormC)
            ? trimmed
            : trimmed.Normalize(NormalizationForm.FormC);

        if (!IsValid(normalized))
        {
            return false;
        }

        tag = normalized;
        return true;
    }

    /// <summary>Case-insensitive key used for dictionaries, DB columns and colour hashing.</summary>
    public static string ToKey(string tag)
    {
        var normalized = tag.IsNormalized(NormalizationForm.FormC) ? tag : tag.Normalize(NormalizationForm.FormC);
        return normalized.ToLowerInvariant();
    }
}
