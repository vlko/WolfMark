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
}
