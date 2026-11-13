using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Notes;
using WolfMark.Core.Tags;
using WolfMark.Storage.Tags;

namespace WolfMark.Storage.Tests;

public sealed class TagRenamerTests
{
    private static readonly DateTime At = new(2026, 10, 15, 14, 3, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Files(EngineFixture f) =>
        [.. Directory.EnumerateFiles(f.Root, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(f.Root, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith(".wolfmark", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private static Task<TagRenamer.Result> RenameAsync(EngineFixture f, IReadOnlyList<NotePath> notes, string tag) =>
        TagRenamer.RenameAsync(notes, tag, (p, t) => f.Engine.ChangeTagAsync(p, t, CancellationToken.None), null);

    [Fact]
    public async Task Finds_the_tag_in_any_letter_case_and_only_in_file_names()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "a", Ct);
        await f.Engine.CreateAsync(At.AddDays(1), "Praca", "b", Ct);
        await f.Engine.CreateAsync(At, "doma", "#praca in the text only", Ct);
        await f.Engine.CreateAsync(At, null, "c", Ct);

        var found = await TagRenamer.FindAsync(f.Engine, "PRACA", Ct);

        Assert.Equal(["2026/10/15/praca_1403.md", "2026/10/16/Praca_1403.md"], found.Select(p => p.RelativePath));
    }

    [Fact]
    public async Task Renames_files_and_keeps_their_content()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "#praca stays", Ct);
        await f.Engine.CreateAsync(At.AddDays(1), "praca", "b", Ct);
        var notes = await TagRenamer.FindAsync(f.Engine, "praca", Ct);

        var result = await RenameAsync(f, notes, "robota");

        Assert.Null(result.Error);
        Assert.Equal(2, result.Renamed);
        Assert.Equal(["2026/10/15/robota_1403.md", "2026/10/16/robota_1403.md"], Files(f));
        Assert.Equal("#praca stays", await File.ReadAllTextAsync(Path.Combine(f.Root, "2026/10/15/robota_1403.md"), Ct));
    }

    [Fact]
    public async Task Merging_into_an_existing_tag_keeps_both_notes_of_the_same_minute()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "old", Ct);
        await f.Engine.CreateAsync(At, "robota", "existing", Ct);
        var notes = await TagRenamer.FindAsync(f.Engine, "praca", Ct);

        var result = await RenameAsync(f, notes, "robota");

        Assert.Null(result.Error);
        var files = Files(f);
        Assert.Equal(2, files.Length);
        Assert.All(files, p => Assert.StartsWith("2026/10/15/robota_1403", p, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_renames_the_done_notes_back()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "a", Ct);
        await f.Engine.CreateAsync(At.AddDays(1), "praca", "b", Ct);
        await f.Engine.CreateAsync(At.AddDays(2), "praca", "c", Ct);
        var before = Files(f);
        var notes = await TagRenamer.FindAsync(f.Engine, "praca", Ct);
        var calls = 0;

        var result = await TagRenamer.RenameAsync(
            notes,
            "robota",
            (p, t) => ++calls == 3 ? throw new IOException("locked") : f.Engine.ChangeTagAsync(p, t, CancellationToken.None),
            null);

        Assert.IsType<IOException>(result.Error);
        Assert.Empty(result.RollbackFailures);
        Assert.Equal(before, Files(f));
    }

    [Fact]
    public async Task Removing_the_tag_keeps_the_notes_untagged()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "content", Ct);
        var notes = await TagRenamer.FindAsync(f.Engine, "praca", Ct);

        var result = await TagRenamer.RenameAsync(notes, null, (p, t) => f.Engine.ChangeTagAsync(p, t, CancellationToken.None), null);

        Assert.Null(result.Error);
        Assert.Equal(["2026/10/15/1403.md"], Files(f));
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(f.Root, "2026/10/15/1403.md"), Ct));
    }

    [Fact]
    public async Task Tag_store_adds_and_removes_tags_without_notes()
    {
        using var dir = new TempDirectory();
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);

        await store.AddAsync("napady", Ct);
        Assert.Contains("napady", store.DefinedTags);

        var reloaded = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await reloaded.LoadAsync(Ct);
        Assert.Contains("napady", reloaded.DefinedTags);

        await reloaded.RemoveAsync("NAPADY", Ct);
        Assert.DoesNotContain("napady", reloaded.DefinedTags);
    }

    [Theory]
    [InlineData("praca", "robota", null)]
    [InlineData("praca", "Praca", null)]
    [InlineData("praca", "praca", TagRenamer.Problem.SameName)]
    [InlineData("praca", "a_b", TagRenamer.Problem.InvalidName)]
    [InlineData("praca", "", TagRenamer.Problem.InvalidName)]
    [InlineData("praca", "claudecode", TagRenamer.Problem.Reserved)]
    [InlineData(TagName.ClaudeCode, "ai", TagRenamer.Problem.Reserved)]
    public void Validates_the_new_name(string oldTag, string newTag, TagRenamer.Problem? expected) =>
        Assert.Equal(expected, TagRenamer.Validate(oldTag, newTag, out _));

    [Fact]
    public async Task Tag_store_keeps_the_colour_on_rename_and_the_target_colour_on_merge()
    {
        using var dir = new TempDirectory();
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        var red = TagColor.FromHex("#D13438");
        var blue = TagColor.FromHex("#0063B1");
        await store.SetColorAsync("praca", red, Ct);
        await store.SetShowInCalendarLabelsAsync("praca", true, Ct);
        await store.SetColorAsync("doma", blue, Ct);
        var paletteBefore = store.GetColor("nastenka");

        await store.RenameAsync("praca", "robota", Ct);
        await store.RenameAsync("robota", "doma", Ct);
        await store.RenameAsync("nastenka", "tabula", Ct);

        var reloaded = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await reloaded.LoadAsync(Ct);
        Assert.Equal(blue, reloaded.GetColor("doma"));
        Assert.Equal(paletteBefore, reloaded.GetColor("tabula")); // a palette colour comes along too
        Assert.DoesNotContain("robota", reloaded.LabelTags);
        Assert.DoesNotContain("praca", reloaded.LabelTags);
    }

    [Fact]
    public async Task Tag_store_rename_moves_labels()
    {
        using var dir = new TempDirectory();
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await store.SetShowInCalendarLabelsAsync("release", true, Ct);

        await store.RenameAsync("release", "vydanie", Ct);

        Assert.True(store.ShowsInCalendarLabels("vydanie"));
        Assert.False(store.ShowsInCalendarLabels("release"));
    }
}
