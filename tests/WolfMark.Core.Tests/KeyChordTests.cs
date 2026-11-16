using WolfMark.Core.Settings;

namespace WolfMark.Core.Tests;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("Shift+Win+N", "Win+Shift+N")]
    [InlineData("ctrl + alt + space", "Ctrl+Alt+Space")]
    [InlineData("Shift+Control+f5", "Ctrl+Shift+F5")]
    [InlineData("Alt+F024", "Alt+F24")]
    [InlineData("Super+1", "Win+1")]
    public void Parses_and_formats_canonically(string text, string expected)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(expected, chord.ToString());
        Assert.True(KeyChord.TryParse(chord.ToString(), out var again));
        Assert.Equal(chord, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N")] // no modifier
    [InlineData("Shift+N")] // Shift alone would take a character from every app
    [InlineData("Ctrl+Alt")] // no key
    [InlineData("Ctrl+N+M")] // two keys
    [InlineData("Ctrl+Ctrl+N")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl++N")]
    public void Rejects_invalid_chords(string? text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Virtual_keys_round_trip()
    {
        Assert.Equal(0x4E, new KeyChord(ChordModifiers.Windows, "N").VirtualKey);
        Assert.Equal(0x20, new KeyChord(ChordModifiers.Control, "Space").VirtualKey);
        Assert.Equal(0x7B, new KeyChord(ChordModifiers.Control, "F12").VirtualKey);
        Assert.Equal(0x35, new KeyChord(ChordModifiers.Control, "5").VirtualKey);
        foreach (var vk in new[] { 0x20, 0x30, 0x39, 0x41, 0x5A, 0x70, 0x87 })
        {
            var key = KeyChord.KeyFromVirtualKey(vk);
            Assert.NotNull(key);
            Assert.Equal(vk, new KeyChord(ChordModifiers.Control, key).VirtualKey);
        }

        Assert.Null(KeyChord.KeyFromVirtualKey(0x1B)); // Esc
        Assert.Null(KeyChord.KeyFromVirtualKey(0x10)); // Shift itself
    }

    [Fact]
    public void Settings_fall_back_to_the_default_on_a_broken_value()
    {
        var s = new QuickNoteSettings { Hotkey = "nonsense" };
        Assert.Equal(KeyChord.QuickNoteDefault, s.Chord);
        Assert.False(new QuickNoteSettings().HotkeyEnabled);
        Assert.Equal("Win+Shift+N", new QuickNoteSettings().Hotkey);
    }
}
