namespace WolfMark.Core.Markdown;

/// <summary>
/// Editing rules that make a line holding only an image (<c>![alt](url)</c>) behave as one block in formatted
/// mode, where its markup is hidden behind the preview: the caret sits before or after the image, text typed
/// there goes to its own line, Backspace/Delete remove the whole image instead of single hidden characters.
/// </summary>
public static class MarkdownImageBlock
{
    /// <summary>True when the line is exactly one image link (surrounding spaces allowed).</summary>
    public static bool IsImageLine(ReadOnlySpan<char> line)
    {
        var t = line.Trim();
        if (t.Length < 5 || !t.StartsWith("![") || t[^1] != ')')
        {
            return false;
        }

        var mid = t.IndexOf("](");
        return mid >= 2 && t[2..mid].IndexOfAny('[', ']') < 0 && t[(mid + 2)..^1].IndexOfAny('(', ')') < 0;
    }

    /// <summary>
    /// After a plain insertion into an image line (typing before or after the hidden markup), moves the typed
    /// text to its own line above or below the image. Returns the fix-up edit on <paramref name="newText"/>, or
    /// null when the change is anything else.
    /// </summary>
    /// <param name="isBlock">Which lines count as image blocks (default <see cref="IsImageLine"/>); the editor
    /// passes only those whose picture is shown.</param>
    public static TextEdit? KeepOnOwnLine(string oldText, string newText, Func<string, bool>? isBlock = null)
    {
        var shorter = Math.Min(oldText.Length, newText.Length);
        var p = oldText.AsSpan(0, shorter).CommonPrefixLength(newText.AsSpan(0, shorter));
        var s = 0;
        while (s < shorter - p && oldText[^(s + 1)] == newText[^(s + 1)])
        {
            s++;
        }

        if (oldText.Length - p - s != 0)
        {
            return null; // something was removed
        }

        var inserted = newText.Substring(p, newText.Length - p - s);
        if (inserted.Length == 0 || inserted.Contains('\n'))
        {
            return null;
        }

        var (lineStart, lineEnd) = MarkdownEditing.LineAt(oldText, p);
        var image = oldText[lineStart..lineEnd];
        if (!IsBlock(image, isBlock))
        {
            return null;
        }

        // At the image's edge only a line break is inserted, so the image line itself stays untouched.
        if (p == lineStart)
        {
            return new TextEdit(p + inserted.Length, 0, "\n", p + inserted.Length, 0);
        }

        if (p == lineEnd)
        {
            return new TextEdit(p, 0, "\n", p + 1 + inserted.Length, 0);
        }

        // Inside the markup (or an ambiguous point between equal characters): rewrite, the nearer side wins.
        var before = p - lineStart < lineEnd - p;
        return before
            ? new TextEdit(lineStart, image.Length + inserted.Length, inserted + "\n" + image, lineStart + inserted.Length, 0)
            : new TextEdit(lineStart, image.Length + inserted.Length, image + "\n" + inserted, lineStart + image.Length + 1 + inserted.Length, 0);
    }

    /// <summary>Backspace with an empty selection: removes an image as a whole, never joins text onto its line.</summary>
    public static TextEdit? Backspace(string text, int caret, Func<string, bool>? isBlock = null)
    {
        var (lineStart, lineEnd) = MarkdownEditing.LineAt(text, caret);
        if (IsBlock(text[lineStart..lineEnd], isBlock))
        {
            if (caret > lineStart)
            {
                return RemoveLine(text, lineStart, lineEnd); // caret after the image
            }

            // Before the image: joining it onto a previous text line would end the block – step over instead.
            return lineStart > 0 && !IsEmptyLine(text, lineStart - 1) ? Caret(lineStart - 1) : null;
        }

        // At the start of a text line right below an image: go behind the image rather than joining the lines.
        if (caret == lineStart && lineStart > 0 && lineEnd > lineStart && IsBlockAt(text, lineStart - 1, isBlock))
        {
            return Caret(lineStart - 1);
        }

        return null;
    }

    /// <summary>Delete with an empty selection: the mirror image of <see cref="Backspace"/>.</summary>
    public static TextEdit? Delete(string text, int caret, Func<string, bool>? isBlock = null)
    {
        var (lineStart, lineEnd) = MarkdownEditing.LineAt(text, caret);
        if (IsBlock(text[lineStart..lineEnd], isBlock))
        {
            if (caret < lineEnd)
            {
                return RemoveLine(text, lineStart, lineEnd); // caret before the image
            }

            return lineEnd < text.Length && !IsEmptyLine(text, lineEnd + 1) ? Caret(lineEnd + 1) : null;
        }

        if (caret == lineEnd && lineEnd < text.Length && lineEnd > lineStart && IsBlockAt(text, lineEnd + 1, isBlock))
        {
            return Caret(lineEnd + 1);
        }

        return null;
    }

    /// <summary>Inserts <paramref name="block"/> (image links) at <paramref name="at"/> on lines of its own.</summary>
    public static TextEdit InsertOnOwnLine(string text, int at, string block)
    {
        at = Math.Clamp(at, 0, text.Length);
        var (lineStart, lineEnd) = MarkdownEditing.LineAt(text, at);
        var insert = (at > lineStart ? "\n" : string.Empty) + block + (at < lineEnd ? "\n" : string.Empty);
        var caret = at + (at > lineStart ? 1 : 0) + block.Length;
        return new TextEdit(at, 0, insert, caret, 0);
    }

    private static TextEdit RemoveLine(string text, int lineStart, int lineEnd)
    {
        if (lineEnd < text.Length)
        {
            return new TextEdit(lineStart, lineEnd - lineStart + 1, string.Empty, lineStart, 0);
        }

        return lineStart > 0
            ? new TextEdit(lineStart - 1, lineEnd - lineStart + 1, string.Empty, lineStart - 1, 0)
            : new TextEdit(lineStart, lineEnd - lineStart, string.Empty, lineStart, 0);
    }

    private static TextEdit Caret(int offset) => new(offset, 0, string.Empty, offset, 0);

    private static bool IsEmptyLine(string text, int offset)
    {
        var (start, end) = MarkdownEditing.LineAt(text, offset);
        return end == start;
    }

    private static bool IsBlock(string line, Func<string, bool>? isBlock) =>
        isBlock is null ? IsImageLine(line) : isBlock(line);

    private static bool IsBlockAt(string text, int offset, Func<string, bool>? isBlock)
    {
        var (start, end) = MarkdownEditing.LineAt(text, offset);
        return IsBlock(text[start..end], isBlock);
    }
}
