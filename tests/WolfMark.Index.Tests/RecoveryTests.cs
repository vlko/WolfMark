using WolfMark.Index.Queries;

namespace WolfMark.Index.Tests;

public sealed class RecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Corrupted_database_is_rebuilt_on_open()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        await f.Indexer.FullScanAsync(null, Ct);
        await f.Db.DisposeAsync();

        await File.WriteAllTextAsync(Path.Combine(f.BaseDir, "index.db"), "this is not a sqlite database at all, just garbage bytes", Ct);
        foreach (var side in new[] { "-wal", "-shm" })
        {
            File.Delete(Path.Combine(f.BaseDir, "index.db" + side));
        }

        await f.ReopenAsync();
        Assert.True(f.Db.WasRebuilt);
        await f.Indexer.FullScanAsync(null, Ct);
        Assert.NotNull(await f.History.GetAsync("2026/10/15/praca_1403.md", Ct));
    }

    [Fact]
    public async Task Deleted_database_is_recreated()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        await f.Indexer.FullScanAsync(null, Ct);
        await f.Db.DisposeAsync();
        foreach (var side in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(Path.Combine(f.BaseDir, "index.db" + side));
        }

        await f.ReopenAsync();
        Assert.True(f.Db.WasRebuilt);
        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Single((await f.History.GetPageAsync(new HistoryQuery(), Ct)).Items);
    }

    [Fact]
    public async Task Reset_rebuilds_from_files()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        f.Write("2026/10/16/praca_1403.md", "# B");
        await f.Indexer.FullScanAsync(null, Ct);

        var resetRaised = false;
        f.Db.Reset += (_, _) => resetRaised = true;
        await f.Db.ResetAsync(Ct);
        Assert.True(resetRaised);
        Assert.Empty((await f.History.GetPageAsync(new HistoryQuery(), Ct)).Items);

        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Equal(2, (await f.History.GetPageAsync(new HistoryQuery(), Ct)).Items.Count);
        Assert.Equal(2, (await f.Calendar.GetMonthAsync(2026, 10, Ct)).Count);
    }
}
