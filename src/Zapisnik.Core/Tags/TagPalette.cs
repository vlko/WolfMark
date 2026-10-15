using System.Text;

namespace Zapisnik.Core.Tags;

/// <summary>
/// Fixed palette of saturated tag colours (SPEC §3.5). The colour of a new tag is chosen
/// deterministically from a stable hash of its key, so two machines without sync agree.
/// </summary>
public static class TagPalette
{
    public static IReadOnlyList<TagColor> Colors { get; } =
    [
        TagColor.FromHex("#3D6FE0"), // blue
        TagColor.FromHex("#F4C531"), // yellow
        TagColor.FromHex("#F28C28"), // orange
        TagColor.FromHex("#E5484D"), // red
        TagColor.FromHex("#3FA65C"), // green
        TagColor.FromHex("#22A6A0"), // teal
        TagColor.FromHex("#8E5BE8"), // purple
        TagColor.FromHex("#E25C9C"), // pink
        TagColor.FromHex("#3AA7E8"), // sky
        TagColor.FromHex("#9CC43A"), // lime
        TagColor.FromHex("#4B4FC4"), // indigo
        TagColor.FromHex("#F2715A"), // coral
        TagColor.FromHex("#A8754A"), // brown
        TagColor.FromHex("#64748B"), // slate
    ];

    /// <summary>Default colour of the reserved <see cref="TagName.ClaudeCode"/> tag.</summary>
    public static TagColor ClaudeCode { get; } = TagColor.FromHex("#D97757");

    /// <summary>Neutral colour for notes without a tag.</summary>
    public static TagColor Untagged { get; } = TagColor.FromHex("#B8B2A7");

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
