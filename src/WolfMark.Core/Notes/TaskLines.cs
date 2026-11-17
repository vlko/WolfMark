namespace WolfMark.Core.Notes;

/// <summary>Ticks an open task off in the text of a note – what the "Open tasks" list does without the editor (M9-01).</summary>
public static class TaskLines
{
    /// <summary>
    /// <paramref name="content"/> with the open task on <paramref name="line"/> marked done (<c>[ ]</c> → <c>[x]</c>),
    /// nothing else touched; null when there is no such open task any more (see <see cref="FindBox"/>).
    /// </summary>
    public static string? Complete(string content, int line, string text) =>
        FindBox(content, line, text) is { } box
            ? string.Concat(content.AsSpan(0, box + 1), "x", content.AsSpan(box + 2))
            : null;

    /// <summary>
    /// Offset of the <c>[ ]</c> box of the open task on <paramref name="line"/> (0-based). The note may have changed
    /// since the list was read: the task is then found by its <paramref name="text"/>, the nearest one when it
    /// repeats. Null when there is no such open task.
    /// </summary>
    public static int? FindBox(string content, int line, string text)
    {
        var tasks = NoteContentAnalyzer.Analyze(content).Tasks;
        var task = tasks.FirstOrDefault(t => t.Line == line && t.Text == text)
                   ?? tasks.Where(t => t.Text == text).MinBy(t => Math.Abs(t.Line - line));
        if (task is null)
        {
            return null;
        }

        var lineStart = 0;
        for (var i = 0; i < task.Line; i++)
        {
            lineStart = content.IndexOf('\n', lineStart) + 1;
        }

        var box = lineStart + task.Column;
        return box + 3 <= content.Length && content.AsSpan(box, 3).SequenceEqual("[ ]") ? box : null;
    }

    /// <summary>Offset of the start of <paramref name="line"/> (0-based); the end of the text when it has fewer lines.</summary>
    public static int LineStart(string content, int line)
    {
        var start = 0;
        for (var i = 0; i < line; i++)
        {
            var next = content.IndexOf('\n', start);
            if (next < 0)
            {
                return content.Length;
            }

            start = next + 1;
        }

        return start;
    }
}
