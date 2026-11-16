namespace WolfMark.Core.Settings;

[Flags]
public enum ChordModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A global keyboard shortcut (M8-01 quick note), stored in the settings as text: <c>Win+Shift+N</c>. Keys are the
/// letters, digits, F1–F24 and Space – the ones with the same virtual-key code on every keyboard layout. At least
/// one of Ctrl, Alt or Win is required: Shift alone would take a character away from every other app.
/// </summary>
public readonly record struct KeyChord(ChordModifiers Modifiers, string Key)
{
    public static KeyChord QuickNoteDefault { get; } = new(ChordModifiers.Windows | ChordModifiers.Shift, "N");

    /// <summary>The Windows virtual-key code of <see cref="Key"/>.</summary>
    public int VirtualKey => ToVirtualKey(Key) ?? 0;

    public override string ToString()
    {
        var parts = new List<string>(5); // the order Windows uses: Win+Ctrl+Alt+Shift+key
        if (Modifiers.HasFlag(ChordModifiers.Windows))
        {
            parts.Add("Win");
        }

        if (Modifiers.HasFlag(ChordModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ChordModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ChordModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>Parses <c>Ctrl+Shift+Space</c> (any order and case, spaces around '+' allowed); false for an invalid chord.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = ChordModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            var modifier = raw.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ChordModifiers.Control,
                "ALT" => ChordModifiers.Alt,
                "SHIFT" => ChordModifiers.Shift,
                "WIN" or "WINDOWS" or "SUPER" or "META" => ChordModifiers.Windows,
                _ => ChordModifiers.None,
            };
            if (modifier != ChordModifiers.None)
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
            }
            else if (key is null && NormalizeKey(raw) is { } normalized)
            {
                key = normalized;
            }
            else
            {
                return false; // a second key, an empty part or an unknown name
            }
        }

        if (key is null)
        {
            return false;
        }

        return TryCreate(modifiers, key, out chord);
    }

    /// <summary>A valid chord from modifiers and a key name (normalized), false when it cannot be a global shortcut.</summary>
    public static bool TryCreate(ChordModifiers modifiers, string key, out KeyChord chord)
    {
        chord = default;
        if (NormalizeKey(key) is not { } normalized
            || (modifiers & (ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Windows)) == ChordModifiers.None)
        {
            return false;
        }

        chord = new KeyChord(modifiers, normalized);
        return true;
    }

    /// <summary>The key name of a Windows virtual-key code (for recording a shortcut), null for keys a chord cannot use.</summary>
    public static string? KeyFromVirtualKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture),
        0x20 => "Space",
        _ => null,
    };

    private static int? ToVirtualKey(string key) => key switch
    {
        "Space" => 0x20,
        [var c] when c is >= 'A' and <= 'Z' or >= '0' and <= '9' => c,
        ['F', .. var n] when int.TryParse(n, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24 => 0x6F + f,
        _ => null,
    };

    private static string? NormalizeKey(string key)
    {
        var upper = key.Trim().ToUpperInvariant();
        var normalized = upper == "SPACE" ? "Space" : upper;
        if (normalized.Length > 1 && normalized[0] == 'F' && normalized.Skip(1).All(char.IsAsciiDigit))
        {
            var number = normalized[1..].TrimStart('0');
            if (number.Length == 0)
            {
                return null; // F0 – not the letter F
            }

            normalized = "F" + number;
        }

        return ToVirtualKey(normalized) is null ? null : normalized;
    }
}
