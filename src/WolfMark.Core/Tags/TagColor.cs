using System.Globalization;

namespace WolfMark.Core.Tags;

/// <summary>sRGB colour of a tag. Serialised as "#RRGGBB".</summary>
public readonly record struct TagColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    public static bool TryParse(string? value, out TagColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        if (span.StartsWith("#"))
        {
            span = span[1..];
        }

        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = new TagColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static TagColor FromHex(string value) =>
        TryParse(value, out var c) ? c : throw new FormatException($"Invalid colour '{value}'.");

    /// <summary>Relative luminance (WCAG).</summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte v)
            {
                var c = v / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));
        }
    }

    /// <summary>True when dark text reads better on this fill than white text.</summary>
    public bool PrefersDarkForeground => Luminance > 0.40;
}
