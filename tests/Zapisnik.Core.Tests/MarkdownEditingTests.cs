using Zapisnik.Core.Markdown;

namespace Zapisnik.Core.Tests;

public sealed class MarkdownEditingTests
{
    private static (string Text, int Sel, int Len) Apply(string text, TextEdit? edit)
    {
        Assert.NotNull(edit);
        return (edit.Value.ApplyTo(text), edit.Value.SelectionStart, edit.Value.SelectionLength);
    }

    [Fact]
    public void Bold_wraps_and_unwraps_selection()
    {
        var (t1, s1, l1) = Apply("a word b", MarkdownEditing.ToggleWrap("a word b", 2, 4, "**"));
        Assert.Equal(("a **word** b", 4, 4), (t1, s1, l1));
        var (t2, _, _) = Apply(t1, MarkdownEditing.ToggleWrap(t1, s1, l1, "**"));
        Assert.Equal("a word b", t2);
    }

    [Fact]
    public void Bold_without_selection_wraps_word()
    {
        var (t, s, _) = Apply("a word b", MarkdownEditing.ToggleWrap("a word b", 4, 0, "**"));
        Assert.Equal(("a **word** b", 6), (t, s));
    }

    [Fact]
    public void Link_selects_url()
    {
        var (t, s, l) = Apply("see docs", MarkdownEditing.InsertLink("see docs", 4, 4));
        Assert.Equal("see [docs](https://)", t);
        Assert.Equal("https://", t.Substring(s, l));
    }

    [Theory]
    [InlineData("todo", 2, "- [ ] todo")]
    [InlineData("- todo", 3, "- [ ] todo")]
    [InlineData("  - [ ] todo", 9, "  - [x] todo")]
    [InlineData("- [x] todo", 8, "- [ ] todo")]
    [InlineData("1. [ ] todo", 8, "1. [x] todo")]
    public void Toggle_task_cycles(string line, int caret, string expected)
    {
        Assert.Equal(expected, Apply(line, MarkdownEditing.ToggleTask(line, caret)).Text);
    }

    [Theory]
    [InlineData("- a", 3, "- a\n- ")]
    [InlineData("  * a", 5, "  * a\n  * ")]
    [InlineData("1. a", 4, "1. a\n2. ")]
    [InlineData("9) a", 4, "9) a\n10) ")]
    [InlineData("- [x] done", 10, "- [x] done\n- [ ] ")]
    [InlineData("> quote", 7, "> quote\n> ")]
    [InlineData("x\n- ", 4, "x\n")]
    [InlineData("- [ ] ", 6, "")]
    public void Enter_continues_or_ends_list(string text, int caret, string expected)
    {
        Assert.Equal(expected, Apply(text, MarkdownEditing.ContinueList(text, caret)).Text);
    }

    [Fact]
    public void Enter_outside_list_is_default()
    {
        Assert.Null(MarkdownEditing.ContinueList("plain", 5));
        Assert.Null(MarkdownEditing.ContinueList("- a", 1));
    }

    [Fact]
    public void Tab_indents_list_lines_only()
    {
        Assert.Null(MarkdownEditing.Indent("plain", 2, 0, outdent: false));
        var text = "- a\n- b\nc";
        var (t, s, _) = Apply(text, MarkdownEditing.Indent(text, 0, 5, outdent: false));
        Assert.Equal("  - a\n  - b\nc", t);
        Assert.Equal(2, s);
        var (back, _, _) = Apply(t, MarkdownEditing.Indent(t, 2, 7, outdent: true));
        Assert.Equal(text, back);
    }
}

public sealed class UndoHistoryTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Typing_is_coalesced_and_undone_in_one_step()
    {
        var time = new ManualTime();
        var history = new UndoHistory(time);
        var text = "";
        foreach (var c in "ahoj")
        {
            var next = text + c;
            history.Record(text, next, text.Length, next.Length);
            text = next;
            time.Now += TimeSpan.FromMilliseconds(100);
        }

        var undo = history.Undo();
        Assert.Equal("", undo!.Value.ApplyTo(text));
        Assert.False(history.CanUndo);

        var redo = history.Redo();
        Assert.Equal("ahoj", redo!.Value.ApplyTo(""));
    }

    [Fact]
    public void Pause_starts_new_step()
    {
        var time = new ManualTime();
        var history = new UndoHistory(time);
        history.Record("", "a", 0, 1);
        time.Now += TimeSpan.FromSeconds(5);
        history.Record("a", "ab", 1, 2);

        Assert.Equal("a", history.Undo()!.Value.ApplyTo("ab"));
        Assert.Equal("", history.Undo()!.Value.ApplyTo("a"));
    }

    [Fact]
    public void Replacement_round_trips()
    {
        var history = new UndoHistory();
        history.Record("hello world", "hello there world", 6, 12);
        history.Seal();
        history.Record("hello there world", "hello there", 17, 11);
        var t = "hello there";
        t = history.Undo()!.Value.ApplyTo(t);
        Assert.Equal("hello there world", t);
        t = history.Undo()!.Value.ApplyTo(t);
        Assert.Equal("hello world", t);
    }
}
