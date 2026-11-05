using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

public sealed class NoteContentAnalyzerTests
{
    [Fact]
    public void A_table_reads_as_its_cells_without_the_delimiter_row()
    {
        const string content = """
            # Endpoints

            | Method | Path | Notes |
            |:--|:--|--:|
            | `GET` | **/invoices** |  |
            """;

        var info = NoteContentAnalyzer.Analyze(content);
        Assert.Equal("Method · Path · Notes GET · /invoices", info.Preview);
    }

    [Fact]
    public void A_table_on_the_first_line_is_a_readable_title() =>
        Assert.Equal("A · B", NoteContentAnalyzer.Analyze("| A | B |\n|---|---|\n| 1 | 2 |").Title);

    [Fact]
    public void Extracts_title_preview_tags_tasks()
    {
        const string content = """
            ---
            tags: [finstat, Praca]
            source: claudecode
            session: abc-123
            ---
            # Hotfix pre **export**

            Riešil som #deploy a #praca.
            - [ ] dopísať testy
            - [x] nasadiť
            ```
            #notatag
            - [ ] not a task
            ```
            """;

        var info = NoteContentAnalyzer.Analyze(content);
        Assert.Equal("Hotfix pre export", info.Title);
        Assert.StartsWith("Riešil som #deploy a #praca. dopísať testy", info.Preview);
        Assert.Equal(["finstat", "Praca", "deploy"], info.Tags);
        Assert.Equal(1, info.OpenTasks);
        Assert.Equal(1, info.DoneTasks);
        Assert.Equal("claudecode", info.Source);
        Assert.Equal("abc-123", info.SessionId);
        Assert.True(info.WordCount >= 10);
    }

    [Fact]
    public void First_line_is_title_without_heading()
    {
        var info = NoteContentAnalyzer.Analyze("\n\nNápady na CODECON\nďalší riadok");
        Assert.Equal("Nápady na CODECON", info.Title);
        Assert.Equal("ďalší riadok", info.Preview);
    }

    [Fact]
    public void Empty_note()
    {
        var info = NoteContentAnalyzer.Analyze(string.Empty);
        Assert.Equal(string.Empty, info.Title);
        Assert.Equal(0, info.WordCount);
    }
}
