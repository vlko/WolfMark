using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

/// <summary>M9-01: the open tasks of a note and ticking one off outside the editor.</summary>
public sealed class TaskLinesTests
{
    [Fact]
    public void Open_tasks_are_listed_with_their_line_and_plain_text()
    {
        var info = NoteContentAnalyzer.Analyze("# Plán\n- [ ] napísať **testy**\n- [x] hotové\n  * [ ] vnorená [úloha](https://x.sk)\n```\n- [ ] kód\n```\n");

        Assert.Equal(
            [new NoteTask(1, 2, "napísať testy"), new NoteTask(3, 4, "vnorená úloha")],
            info.Tasks);
    }

    [Fact]
    public void Lines_count_from_the_start_of_the_file_with_front_matter()
    {
        var info = NoteContentAnalyzer.Analyze("---\nsource: x\n---\n- [ ] prvá\n");

        Assert.Equal(3, Assert.Single(info.Tasks).Line);
    }

    [Fact]
    public void Completing_changes_only_the_box()
    {
        var content = "# Plán\r\n- [ ] prvá\r\n- [ ] druhá\r\n";

        Assert.Equal("# Plán\r\n- [ ] prvá\r\n- [x] druhá\r\n", TaskLines.Complete(content, 2, "druhá"));
    }

    /// <summary>The list was read before the note changed: the task is where its text is now.</summary>
    [Fact]
    public void A_task_that_moved_is_found_by_its_text()
    {
        var content = "nový riadok\nďalší\n- [ ] prvá\n- [ ] druhá\n";

        Assert.Equal("nový riadok\nďalší\n- [x] prvá\n- [ ] druhá\n", TaskLines.Complete(content, 0, "prvá"));
    }

    [Fact]
    public void Of_two_tasks_with_the_same_text_the_one_on_the_line_is_completed()
    {
        var content = "- [ ] zavolať\n- [ ] zavolať\n";

        Assert.Equal("- [ ] zavolať\n- [x] zavolať\n", TaskLines.Complete(content, 1, "zavolať"));
    }

    [Theory]
    [InlineData("- [x] prvá\n")] // done meanwhile
    [InlineData("- [ ] iná\n")] // rewritten
    [InlineData("```\n- [ ] prvá\n```\n")] // not a task
    [InlineData("")]
    public void Nothing_is_changed_when_the_task_is_no_longer_there(string content)
    {
        Assert.Null(TaskLines.Complete(content, 0, "prvá"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(9, 7)]
    public void Line_start(int line, int expected)
    {
        Assert.Equal(expected, TaskLines.LineStart("a\nbc\nde", line));
    }
}
