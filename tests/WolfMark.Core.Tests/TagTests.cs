using WolfMark.Core.Tags;

namespace WolfMark.Core.Tests;

public sealed class TagTests
{
    [Theory]
    [InlineData("praca", true)]
    [InlineData("Práca-2026", true)]
    [InlineData("日本", true)]
    [InlineData("a_b", false)]
    [InlineData("a b", false)]
    [InlineData("a?b", false)]
    [InlineData("", false)]
    public void Validates_tags(string tag, bool valid) => Assert.Equal(valid, TagName.IsValid(tag));

    [Fact]
    public void Normalize_strips_hash_and_trims()
    {
        Assert.True(TagName.TryNormalize("  #praca ", out var t));
        Assert.Equal("praca", t);
    }

    [Fact]
    public void Palette_choice_is_deterministic_and_case_insensitive()
    {
        // Values must never change across versions/machines: FNV-1a of the lower-case UTF-8 key.
        Assert.Equal(TagPalette.ForTag("praca"), TagPalette.ForTag("PRACA"));
        Assert.Equal(TagPalette.Colors[(int)(TagPalette.StableHash("praca") % 14)], TagPalette.ForTag("praca"));
        Assert.Equal(TagPalette.ClaudeCode, TagPalette.ForTag("claudecode"));
    }

    [Fact]
    public void Stable_hash_matches_reference_fnv1a()
    {
        // Reference FNV-1a 32-bit values.
        Assert.Equal(0x811C9DC5u, TagPalette.StableHash(string.Empty));
        Assert.Equal(0xE40C292Cu, TagPalette.StableHash("a"));
        Assert.Equal(0xBF9CF968u, TagPalette.StableHash("foobar"));
    }

    [Fact]
    public void Colour_parsing_round_trips()
    {
        Assert.True(TagColor.TryParse("#3D6FE0", out var c));
        Assert.Equal("#3D6FE0", c.ToHex());
        Assert.False(TagColor.TryParse("#3D6FE", out _));
        Assert.True(TagColor.FromHex("#F4C531").PrefersDarkForeground);
        Assert.False(TagColor.FromHex("#4B4FC4").PrefersDarkForeground);
    }
}
