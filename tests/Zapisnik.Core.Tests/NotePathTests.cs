using Zapisnik.Core.Notes;

namespace Zapisnik.Core.Tests;

public sealed class NotePathTests
{
    [Fact]
    public void Standard_path_has_date_and_name()
    {
        var path = NotePath.Parse("2026\\10\\15\\praca_1403.md");
        Assert.True(path.IsStandard);
        Assert.Equal(new DateOnly(2026, 10, 15), path.Date);
        Assert.Equal("praca", path.Tag);
        Assert.Equal("2026/10/15/praca_1403.md", path.RelativePath);
        Assert.Equal("2026/10/15", path.Directory);
        Assert.Equal(new DateTime(2026, 10, 15, 14, 3, 0), path.CreatedAt);
    }

    [Theory]
    [InlineData("2026/10/15/nakupy.md")]
    [InlineData("README.md")]
    [InlineData("2026/13/01/0900.md")]
    [InlineData("2026/02/30/0900.md")]
    [InlineData("misc/2026/10/15/0900.md")]
    public void Non_standard_paths_are_still_notes(string rel)
    {
        Assert.True(NotePath.TryParse(rel, out var path));
        Assert.False(path.IsStandard);
        Assert.Null(path.CreatedAt);
    }

    [Theory]
    [InlineData("2026/10/15/.praca_1403.md.tmp")]
    [InlineData("2026/10/15/.praca_1403.md")]
    [InlineData("2026/10/15/assets/x.md")]
    [InlineData(".zapisnik/tags.json")]
    [InlineData(".zapisnik/notes.md")]
    [InlineData("2026/10/15/image.png")]
    [InlineData("2026/10/15/~$draft.md")]
    public void Ignores_internal_and_temp_files(string rel)
    {
        Assert.False(NotePath.TryParse(rel, out _));
    }

    [Fact]
    public void Create_formats_directory()
    {
        var p = NotePath.Create(new DateOnly(2026, 1, 5), NoteName.Create(null, new TimeOnly(7, 0)));
        Assert.Equal("2026/01/05/0700.md", p.RelativePath);
    }
}
