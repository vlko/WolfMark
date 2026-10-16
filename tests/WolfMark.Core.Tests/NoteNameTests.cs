using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

public sealed class NoteNameTests
{
    [Theory]
    [InlineData("0927.md", null, 9, 27, 0, false, 0, null)]
    [InlineData("praca_1403.md", "praca", 14, 3, 0, false, 0, null)]
    [InlineData("praca_140312.md", "praca", 14, 3, 12, true, 0, null)]
    [InlineData("praca_140312-2.md", "praca", 14, 3, 12, true, 2, null)]
    [InlineData("praca_1403-3.md", "praca", 14, 3, 0, false, 3, null)]
    [InlineData("ClaudeCode_1655.md", "ClaudeCode", 16, 55, 0, false, 0, null)]
    [InlineData("pracovné-veci_0000.md", "pracovné-veci", 0, 0, 0, false, 0, null)]
    [InlineData("praca_1403-NAZOVPC.md", "praca", 14, 3, 0, false, 0, "NAZOVPC")]
    [InlineData("praca_1403-2-NAZOVPC.md", "praca", 14, 3, 0, false, 2, "NAZOVPC")]
    [InlineData("2359-MYPC-2.md", null, 23, 59, 0, false, 0, "MYPC-2")]
    [InlineData("Praca_1403.MD", "Praca", 14, 3, 0, false, 0, null)]
    public void Parses_valid_names(string file, string? tag, int h, int m, int s, bool hasSeconds, int suffix, string? conflict)
    {
        Assert.True(NoteName.TryParse(file, out var name));
        Assert.Equal(tag, name.Tag);
        Assert.Equal(new TimeOnly(h, m, s), name.Time);
        Assert.Equal(hasSeconds, name.HasSeconds);
        Assert.Equal(suffix, name.Suffix);
        Assert.Equal(conflict, name.ConflictMarker);
        Assert.Equal(conflict is not null, name.IsConflictCopy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notes.md")]
    [InlineData("praca_1403.txt")]
    [InlineData("praca_2403.md")] // hour 24
    [InlineData("praca_1460.md")] // minute 60
    [InlineData("praca_140360.md")] // second 60
    [InlineData("praca_14031.md")] // 5 digits
    [InlineData("praca_1403-1.md")] // suffix 1 is never generated
    [InlineData("praca_1403-02.md")] // leading zero
    [InlineData("praca_1403-.md")]
    [InlineData("praca_1403-2-.md")]
    [InlineData("pra ca_1403.md")] // space in tag
    [InlineData("pr:aca_1403.md")]
    [InlineData("_1403.md")] // empty tag
    [InlineData("a_b_1403.md")] // underscore in tag
    [InlineData(".md")]
    [InlineData("praca_1403x.md")]
    public void Rejects_invalid_names(string file)
    {
        Assert.False(NoteName.TryParse(file, out _));
    }

    [Theory]
    [InlineData("0927.md")]
    [InlineData("praca_1403.md")]
    [InlineData("praca_140312.md")]
    [InlineData("praca_140312-2.md")]
    [InlineData("praca_140312-17.md")]
    [InlineData("žltá-ruža_0805.md")]
    [InlineData("praca_1403-NAZOVPC.md")]
    [InlineData("praca_1403-2-NAZOVPC.md")]
    public void Round_trip_is_identity(string file)
    {
        Assert.True(NoteName.TryParse(file, out var name));
        Assert.Equal(file, name.FileName);
    }

    [Fact]
    public void Tag_is_normalized_to_nfc()
    {
        var decomposed = "práca_1403.md".Normalize(System.Text.NormalizationForm.FormD);
        Assert.True(NoteName.TryParse(decomposed, out var name));
        Assert.True(name.Tag!.IsNormalized(System.Text.NormalizationForm.FormC));
    }

    [Fact]
    public void Collision_sequence_adds_seconds_then_suffixes()
    {
        var name = NoteName.Create("praca", new TimeOnly(14, 3));
        var c1 = name.NextCollisionCandidate(12);
        var c2 = c1.NextCollisionCandidate(40);
        var c3 = c2.NextCollisionCandidate(40);

        Assert.Equal("praca_1403.md", name.FileName);
        Assert.Equal("praca_140312.md", c1.FileName);
        Assert.Equal("praca_140312-2.md", c2.FileName);
        Assert.Equal("praca_140312-3.md", c3.FileName);
    }

    [Fact]
    public void Changing_tag_keeps_time()
    {
        var name = NoteName.Create(null, new TimeOnly(9, 27));
        Assert.Equal("praca_0927.md", name.WithTag("praca").FileName);
        Assert.Equal("0927.md", name.WithTag("praca").WithTag(null).FileName);
    }

    [Fact]
    public void Create_rejects_invalid_tag()
    {
        Assert.Throws<ArgumentException>(() => NoteName.Create("a_b", new TimeOnly(1, 2)));
        Assert.Throws<ArgumentException>(() => NoteName.Create("a/b", new TimeOnly(1, 2)));
    }

    [Fact]
    public void Conflict_copy_points_to_original()
    {
        Assert.True(NotePath.TryParse("2026/10/15/praca_1403-PC.md", out var path));
        Assert.True(path.IsConflictCopy);
        Assert.Equal("2026/10/15/praca_1403.md", path.ConflictOriginal!.RelativePath);
    }
}
