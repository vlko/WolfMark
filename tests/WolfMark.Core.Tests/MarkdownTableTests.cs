using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownTableTests
{
    private static List<MarkdownTable> Find(string text, params int[] codeLines)
    {
        var lines = text.Split('\n');
        return MarkdownTable.Find(lines.Length, i => lines[i], codeLines.Contains);
    }

    private static string[] CellTexts(string row) =>
        [.. MarkdownTable.Cells(row).Select(c => row.Substring(c.Start, c.Length))];

    [Fact]
    public void Finds_a_table_with_its_rows_and_alignment()
    {
        var tables = Find("intro\n| A | B | C |\n|:--|:-:|--:|\n| 1 | 2 | 3 |\n| 4 | 5 | 6 |\nafter");

        var table = Assert.Single(tables);
        Assert.Equal(1, table.FirstLine);
        Assert.Equal(4, table.LineCount);
        Assert.Equal(3, table.RowCount);
        Assert.Equal([TableAlign.Left, TableAlign.Center, TableAlign.Right], table.Aligns);
        Assert.Equal(4, table.LineOfRow(2));
    }

    [Fact]
    public void Ends_at_a_blank_line_or_a_line_without_a_pipe()
    {
        Assert.Equal(3, Assert.Single(Find("A | B\n--|--\n1 | 2\n\n3 | 4")).LineCount);
        Assert.Equal(2, Assert.Single(Find("A | B\n--|--\njust text\n1 | 2")).LineCount);
    }

    [Theory]
    [InlineData("A | B\n--|--|--")] // header and delimiter differ in cell count
    [InlineData("A B\n---")] // no pipe: a setext heading, not a table
    [InlineData("A | B\n-- | x")] // not a delimiter row
    [InlineData("| A |\n")]
    public void Not_a_table(string text) => Assert.Empty(Find(text));

    [Fact]
    public void Code_blocks_hold_no_table() => Assert.Empty(Find("```\nA | B\n--|--\n```", 0, 1, 2, 3));

    [Fact]
    public void Finds_two_tables()
    {
        var tables = Find("A|B\n-|-\n1|2\n\nC|D\n-|-");

        Assert.Equal([0, 4], tables.Select(t => t.FirstLine));
    }

    [Theory]
    [InlineData("| a | b |", new[] { "a", "b" })]
    [InlineData("a | b", new[] { "a", "b" })]
    [InlineData("  | a |  b  ", new[] { "a", "b" })]
    [InlineData("| a |  |", new[] { "a", "" })]
    [InlineData(@"| a \| b | c |", new[] { @"a \| b", "c" })]
    [InlineData("| `x` | **y** |", new[] { "`x`", "**y**" })]
    public void Splits_cells(string row, string[] expected) => Assert.Equal(expected, CellTexts(row));

    [Fact]
    public void Columns_keep_their_width_when_they_fit() =>
        Assert.Equal([50, 80], MarkdownTable.FitColumns([50, 80], 200, 30));

    [Fact]
    public void Narrow_columns_keep_their_width_and_wide_ones_share_the_rest() =>
        Assert.Equal([40, 130, 130], MarkdownTable.FitColumns([40, 500, 300], 300, 30));

    [Fact]
    public void Cell_tokens_are_clipped_and_shifted()
    {
        MarkdownToken[] tokens = [new(2, 8, MarkdownTokenKind.Strong), new(12, 3, MarkdownTokenKind.Emphasis)];

        Assert.Equal([new MarkdownToken(1, 5, MarkdownTokenKind.Strong)], MarkdownTable.CellTokens(tokens, (1, 6)));
    }

    [Fact]
    public void Cells_are_source_ranges()
    {
        var cells = MarkdownTable.Cells("| ab | c |");

        Assert.Equal([(2, 2), (7, 1)], cells);
    }
}
