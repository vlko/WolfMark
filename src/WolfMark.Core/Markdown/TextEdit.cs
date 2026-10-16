namespace WolfMark.Core.Markdown;

/// <summary>
/// A replacement of <see cref="Length"/> characters at <see cref="Start"/> with <see cref="Insert"/>,
/// followed by a new selection. Editors apply it to a sub-range so undo and formatting stay local.
/// </summary>
public readonly record struct TextEdit(int Start, int Length, string Insert, int SelectionStart, int SelectionLength)
{
    public string ApplyTo(string text) => string.Concat(text.AsSpan(0, Start), Insert, text.AsSpan(Start + Length));
}
