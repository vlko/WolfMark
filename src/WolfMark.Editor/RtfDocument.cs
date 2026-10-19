using System.Globalization;
using System.Text;
using Microsoft.UI.Text;
using Windows.UI;

namespace WolfMark.Editor;

/// <summary>Character format a Markdown token sets – on a RichEdit range or on characters of an RTF document.</summary>
internal interface ICharFormatSink
{
    string Name { set; }

    float Size { set; }

    Color Foreground { set; }

    Color Background { set; }

    bool Bold { set; }

    bool Italic { set; }

    bool Strikethrough { set; }

    bool Underline { set; }

    bool Hidden { set; }
}

/// <summary>Format of a RichEdit range.</summary>
internal sealed class RangeFormat(ITextCharacterFormat f) : ICharFormatSink
{
    public string Name { set => f.Name = value; }

    public float Size { set => f.Size = RtfDocument.HalfPoints(value); }

    public Color Foreground { set => f.ForegroundColor = value; }

    public Color Background { set => f.BackgroundColor = value; }

    public bool Bold { set => f.Bold = On(value); }

    public bool Italic { set => f.Italic = On(value); }

    public bool Strikethrough { set => f.Strikethrough = On(value); }

    public bool Underline { set => f.Underline = value ? UnderlineType.Single : UnderlineType.None; }

    public bool Hidden { set => f.Hidden = On(value); }

    private static FormatEffect On(bool value) => value ? FormatEffect.On : FormatEffect.Off;
}

internal record struct CharStyle(
    string Name, float Size, Color Foreground, Color Background,
    bool Bold, bool Italic, bool Strikethrough, bool Underline, bool Hidden);

/// <summary>Format of characters [start, end) of a line being written as RTF.</summary>
internal sealed class CharRangeFormat(CharStyle[] chars, int start, int end) : ICharFormatSink
{
    public string Name { set => Set(c => c with { Name = value }); }

    public float Size { set => Set(c => c with { Size = RtfDocument.HalfPoints(value) }); }

    public Color Foreground { set => Set(c => c with { Foreground = value }); }

    public Color Background { set => Set(c => c with { Background = value }); }

    public bool Bold { set => Set(c => c with { Bold = value }); }

    public bool Italic { set => Set(c => c with { Italic = value }); }

    public bool Strikethrough { set => Set(c => c with { Strikethrough = value }); }

    public bool Underline { set => Set(c => c with { Underline = value }); }

    public bool Hidden { set => Set(c => c with { Hidden = value }); }

    private void Set(Func<CharStyle, CharStyle> change)
    {
        for (var i = Math.Max(0, start); i < Math.Min(end, chars.Length); i++)
        {
            chars[i] = change(chars[i]);
        }
    }
}

/// <summary>
/// A whole formatted note as RTF, so opening a note is one <c>SetText</c> instead of thousands of range format
/// calls. Every run states its full format; paragraph marks keep the base format (as after a reset).
/// </summary>
internal sealed class RtfDocument(CharStyle basis)
{
    private readonly List<string> _fonts = [];
    private readonly Dictionary<Color, int> _colors = [];
    private readonly StringBuilder _body = new();
    private bool _first = true;

    /// <summary>RTF sizes are whole half points; the range path rounds the same way so both look alike.</summary>
    public static float HalfPoints(float points) => MathF.Round(points * 2) / 2;

    public void AddLine(string text, CharStyle[] chars)
    {
        if (!_first)
        {
            _body.Append("\\par\r\n");
        }

        _first = false;
        var i = 0;
        while (i < text.Length)
        {
            var style = chars[i];
            var end = i + 1;
            while (end < text.Length && chars[end] == style)
            {
                end++;
            }

            AppendRun(text.AsSpan(i, end - i), style);
            i = end;
        }
    }

    /// <summary>
    /// <paramref name="extraParagraph"/> adds one more paragraph mark at the end: RichEdit may fold the last one
    /// into the document's own final mark, which loses a trailing line break.
    /// </summary>
    public string Build(bool extraParagraph)
    {
        var head = new StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1{\\fonttbl");
        var baseFont = Font(basis.Name);
        var baseColor = ColorIndex(basis.Foreground);
        var body = _body.ToString();
        for (var f = 0; f < _fonts.Count; f++)
        {
            head.Append(CultureInfo.InvariantCulture, $"{{\\f{f}\\fnil\\fcharset0 ");
            Escape(head, _fonts[f]);
            head.Append(";}");
        }

        head.Append("}{\\colortbl ;");
        foreach (var color in _colors.OrderBy(c => c.Value).Select(c => c.Key))
        {
            head.Append(CultureInfo.InvariantCulture, $"\\red{color.R}\\green{color.G}\\blue{color.B};");
        }

        head.Append('}');
        head.Append(CultureInfo.InvariantCulture, $"\\pard\\sa0\\sb0\\f{baseFont}\\fs{(int)(basis.Size * 2)}\\cf{baseColor} ");
        head.Append(body);
        if (extraParagraph)
        {
            head.Append("\\par ");
        }

        head.Append('}');
        return head.ToString();
    }

    private void AppendRun(ReadOnlySpan<char> text, CharStyle s)
    {
        var b = _body;
        b.Append(CultureInfo.InvariantCulture, $"{{\\f{Font(s.Name)}\\fs{(int)(s.Size * 2)}\\cf{ColorIndex(s.Foreground)}");
        if (s.Background.A > 0)
        {
            b.Append(CultureInfo.InvariantCulture, $"\\highlight{ColorIndex(s.Background)}");
        }

        if (s.Bold)
        {
            b.Append("\\b");
        }

        if (s.Italic)
        {
            b.Append("\\i");
        }

        if (s.Strikethrough)
        {
            b.Append("\\strike");
        }

        if (s.Underline)
        {
            b.Append("\\ul");
        }

        if (s.Hidden)
        {
            b.Append("\\v");
        }

        b.Append(' ');
        Escape(b, text);
        b.Append('}');
    }

    private static void Escape(StringBuilder b, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\' or '{' or '}':
                    b.Append('\\').Append(c);
                    break;
                case '\t':
                    b.Append("\\tab ");
                    break;
                case < ' ' or > '~':
                    b.Append(CultureInfo.InvariantCulture, $"\\u{(short)c}?");
                    break;
                default:
                    b.Append(c);
                    break;
            }
        }
    }

    private int Font(string name)
    {
        var index = _fonts.IndexOf(name);
        if (index < 0)
        {
            _fonts.Add(name);
            index = _fonts.Count - 1;
        }

        return index;
    }

    private int ColorIndex(Color color)
    {
        color.A = 255;
        if (!_colors.TryGetValue(color, out var index))
        {
            index = _colors.Count + 1; // 0 is "auto"
            _colors[color] = index;
        }

        return index;
    }
}
