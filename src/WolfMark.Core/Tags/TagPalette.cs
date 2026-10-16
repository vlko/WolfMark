using System.Text;

namespace WolfMark.Core.Tags;

/// <summary>
/// Fixed palette of saturated tag colours (SPEC §3.5). The colour of a new tag is chosen
/// deterministically from a stable hash of its key, so two machines without sync agree.
/// </summary>
public static class TagPalette
{
    public static IReadOnlyList<TagColor> Colors { get; } =
    [
        TagColor.FromHex("#1E88F5"), // blue
        TagColor.FromHex("#F7C62F"), // yellow
        TagColor.FromHex("#F5922A"), // orange
        TagColor.FromHex("#F2564B"), // red
        TagColor.FromHex("#5CCB86"), // green
        TagColor.FromHex("#12A5B5"), // teal
        TagColor.FromHex("#A47BF2"), // purple
        TagColor.FromHex("#F07AB8"), // pink
        TagColor.FromHex("#45BDF2"), // sky
        TagColor.FromHex("#9FD13F"), // lime
        TagColor.FromHex("#4C5FD7"), // indigo
        TagColor.FromHex("#F57F72"), // salmon
        TagColor.FromHex("#C08552"), // brown
        TagColor.FromHex("#6F8197"), // slate
    ];

    /// <summary>Default colour of the reserved <see cref="TagName.ClaudeCode"/> tag.</summary>
    public static TagColor ClaudeCode { get; } = TagColor.FromHex("#D97757");

    /// <summary>Neutral colour for notes without a tag.</summary>
    public static TagColor Untagged { get; } = TagColor.FromHex("#C6C2B8");

    public static TagColor ForTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Untagged;
        }

        var key = TagName.ToKey(tag);
        if (key == TagName.ToKey(TagName.ClaudeCode))
        {
            return ClaudeCode;
        }

        return Colors[(int)(StableHash(key) % (uint)Colors.Count)];
    }

    /// <summary>FNV-1a over UTF-8. Never use string.GetHashCode here – it is randomised per process.</summary>
    internal static uint StableHash(string key)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        Span<byte> buffer = stackalloc byte[256];
        var bytes = Encoding.UTF8.GetByteCount(key) <= buffer.Length
            ? buffer[..Encoding.UTF8.GetBytes(key, buffer)]
            : Encoding.UTF8.GetBytes(key);

        var hash = offset;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }
}
