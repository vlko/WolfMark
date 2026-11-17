using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Tags;
using WolfMark.Storage.Tags;

namespace WolfMark.Storage.Tests;

public sealed class TagStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_clean_machines_pick_the_same_colour()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        var storeA = new TagStore(a.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        var storeB = new TagStore(b.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await storeA.LoadAsync(Ct);
        await storeB.LoadAsync(Ct);

        Assert.Equal(storeA.GetColor("nový-tag"), storeB.GetColor("NOVÝ-TAG"));
        Assert.Equal(TagPalette.ForTag("nový-tag"), storeA.GetColor("nový-tag"));
    }

    [Fact]
    public async Task Overrides_persist_and_reload()
    {
        using var dir = new TempDirectory();
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await store.LoadAsync(Ct);
        await store.SetColorAsync("praca", TagColor.FromHex("#112233"), Ct);
        await store.SetShowInCalendarLabelsAsync("Praca", true, Ct);

        var reloaded = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await reloaded.LoadAsync(Ct);
        Assert.Equal("#112233", reloaded.GetColor("PRACA").ToHex());
        Assert.True(reloaded.ShowsInCalendarLabels("praca"));
        Assert.Equal(["praca"], reloaded.LabelTags);
    }

    [Fact]
    public async Task Broken_file_falls_back_to_palette()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".wolfmark"));
        await File.WriteAllTextAsync(Path.Combine(dir.Path, ".wolfmark", "tags.json"), "{ not json", Ct);
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await store.LoadAsync(Ct);
        Assert.Equal(TagPalette.ForTag("x"), store.GetColor("x"));
    }

    [Fact]
    public async Task Broken_file_is_kept_as_a_copy_when_the_first_change_replaces_it()
    {
        using var dir = new TempDirectory();
        var folder = Path.Combine(dir.Path, ".wolfmark");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "tags.json"), "{ \"tags\": [ <<<<<<< merge", Ct);
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await store.LoadAsync(Ct);
        Assert.Empty(Directory.GetFiles(folder, "tags.broken-*")); // nothing is touched until something changes

        await store.SetColorAsync("praca", TagColor.FromHex("#112233"), Ct);
        await store.SetColorAsync("rodina", TagColor.FromHex("#445566"), Ct);

        var copy = Assert.Single(Directory.GetFiles(folder, "tags.broken-*.json"));
        Assert.Contains("<<<<<<< merge", await File.ReadAllTextAsync(copy, Ct));
        var reloaded = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await reloaded.LoadAsync(Ct);
        Assert.Equal("#112233", reloaded.GetColor("praca").ToHex());
    }

    [Fact]
    public async Task Changes_made_at_once_all_end_up_in_the_file()
    {
        using var dir = new TempDirectory();
        var store = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await store.LoadAsync(Ct);
        var tags = Enumerable.Range(0, 40).Select(i => $"tag{i}").ToArray();

        await Task.WhenAll(tags.Select(t => Task.Run(() => store.SetColorAsync(t, TagColor.FromHex("#112233"), Ct), Ct)));

        var reloaded = new TagStore(dir.Path, new SelfWriteRegistry(), NullLogger<TagStore>.Instance);
        await reloaded.LoadAsync(Ct);
        Assert.Equal(tags.Order(), reloaded.DefinedTags.Order());
        Assert.Empty(Directory.GetFiles(Path.Combine(dir.Path, ".wolfmark"), "*.tmp"));
    }
}
