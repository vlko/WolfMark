using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

public sealed class NotesTreeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "WolfMarkNotes");

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void A_note_under_the_root_is_found_by_its_relative_path()
    {
        Assert.True(NotesTree.TryGetNotePath(Root, At("2026", "11", "05", "praca_0900.md"), out var path));
        Assert.Equal("2026/11/05/praca_0900.md", path.RelativePath);
        Assert.True(path.IsStandard);
    }

    [Fact]
    public void A_markdown_file_elsewhere_in_the_tree_is_a_non_standard_note()
    {
        Assert.True(NotesTree.TryGetNotePath(Root, At("projekty", "plan.md"), out var path));
        Assert.False(path.IsStandard);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_outside()
    {
        Assert.False(NotesTree.TryGetNotePath(Root, Root + "2" + Path.DirectorySeparatorChar + "plan.md", out _));
        Assert.False(NotesTree.Contains(Root, Root + "2" + Path.DirectorySeparatorChar + "plan.md"));
    }

    [Theory]
    [InlineData("assets")]
    [InlineData(".trash")]
    public void Files_in_assets_or_hidden_folders_are_not_notes(string folder) =>
        Assert.False(NotesTree.TryGetNotePath(Root, At("2026", folder, "a.md"), out _));

    [Fact]
    public void Other_files_and_the_root_itself_are_not_notes()
    {
        Assert.False(NotesTree.TryGetNotePath(Root, At("readme.txt"), out _));
        Assert.False(NotesTree.Contains(Root, Root));
    }

    [Fact]
    public void A_trailing_separator_on_the_root_does_not_matter() =>
        Assert.True(NotesTree.TryGetNotePath(Root + Path.DirectorySeparatorChar, At("a.md"), out _));

    [Fact]
    public void Case_matters_only_on_windows()
    {
        var upper = Path.Combine(Root.ToUpperInvariant(), "a.md");
        Assert.Equal(OperatingSystem.IsWindows(), NotesTree.Contains(Root, upper));
    }
}
