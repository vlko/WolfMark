namespace WolfMark.Core.Markdown;

/// <summary>
/// Text-level undo/redo for the editor (SPEC §4.4). Stores minimal diffs between snapshots and
/// coalesces fast typing into one step, so syntax-highlighting format changes never enter the
/// undo stack (the native RichEdit undo is disabled).
/// </summary>
public sealed class UndoHistory(TimeProvider timeProvider, int capacity = 500)
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(1200);

    private readonly LinkedList<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private DateTimeOffset _lastRecord;

    public UndoHistory()
        : this(TimeProvider.System)
    {
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Records the change from <paramref name="oldText"/> to <paramref name="newText"/>.</summary>
    public void Record(string oldText, string newText, int selectionBefore, int selectionAfter)
    {
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return;
        }

        var (start, removed, inserted) = Diff(oldText, newText);
        RecordEdit(start, removed, inserted, selectionBefore, selectionAfter);
    }

    /// <summary>Records a known edit: <paramref name="removed"/> at <paramref name="start"/> became <paramref name="inserted"/>.</summary>
    public void RecordEdit(int start, string removed, string inserted, int selectionBefore, int selectionAfter)
    {
        if (removed.Length == 0 && inserted.Length == 0)
        {
            return;
        }

        var step = new Step(start, removed, inserted, selectionBefore, selectionAfter);
        var now = timeProvider.GetUtcNow();
        _redo.Clear();

        if (_undo.Last is { } last && now - _lastRecord < CoalesceWindow && TryMerge(last.Value, step, out var merged))
        {
            last.Value = merged;
        }
        else
        {
            _undo.AddLast(step);
            if (_undo.Count > capacity)
            {
                _undo.RemoveFirst();
            }
        }

        _lastRecord = now;
    }

    /// <summary>Breaks coalescing (e.g. after caret jumps or a paste).</summary>
    public void Seal() => _lastRecord = DateTimeOffset.MinValue;

    public TextEdit? Undo()
    {
        if (_undo.Last is not { } node)
        {
            return null;
        }

        _undo.RemoveLast();
        var s = node.Value;
        _redo.Push(s);
        Seal();
        return new TextEdit(s.Start, s.Inserted.Length, s.Removed, s.SelectionBefore, 0);
    }

    public TextEdit? Redo()
    {
        if (!_redo.TryPop(out var s))
        {
            return null;
        }

        _undo.AddLast(s);
        Seal();
        return new TextEdit(s.Start, s.Removed.Length, s.Inserted, s.SelectionAfter, 0);
    }

    internal static (int Start, string Removed, string Inserted) Diff(string a, string b)
    {
        var prefix = 0;
        var max = Math.Min(a.Length, b.Length);
        while (prefix < max && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        return (prefix, a.Substring(prefix, a.Length - prefix - suffix), b.Substring(prefix, b.Length - prefix - suffix));
    }

    private static bool TryMerge(Step previous, Step next, out Step merged)
    {
        merged = previous;

        // Continued typing: insertion directly after the previous insertion, no removal, no line break.
        if (previous.Removed.Length == 0 && next.Removed.Length == 0
            && next.Start == previous.Start + previous.Inserted.Length
            && !next.Inserted.Contains('\n') && !(previous.Inserted.EndsWith(' ') && !next.Inserted.StartsWith(' ')))
        {
            merged = previous with { Inserted = previous.Inserted + next.Inserted, SelectionAfter = next.SelectionAfter };
            return true;
        }

        // Continued backspacing.
        if (previous.Inserted.Length == 0 && next.Inserted.Length == 0 && next.Start + next.Removed.Length == previous.Start)
        {
            merged = previous with { Start = next.Start, Removed = next.Removed + previous.Removed, SelectionAfter = next.SelectionAfter };
            return true;
        }

        return false;
    }

    private sealed record Step(int Start, string Removed, string Inserted, int SelectionBefore, int SelectionAfter);
}
