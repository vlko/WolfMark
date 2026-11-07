namespace WolfMark.Index.Tests;

public sealed class BackupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_is_a_complete_database_that_opens_elsewhere()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        await f.Indexer.FullScanAsync(null, Ct);
        var copyDir = Path.Combine(f.BaseDir, "copy");
        Directory.CreateDirectory(copyDir);
        await File.WriteAllTextAsync(Path.Combine(copyDir, "index.db-wal"), "stale", Ct);

        await f.Db.BackupToAsync(Path.Combine(copyDir, "index.db"), Ct);
        await f.Db.BackupToAsync(Path.Combine(copyDir, "index.db"), Ct); // replaces an older copy

        Assert.False(File.Exists(Path.Combine(copyDir, "index.db-wal")));
        await using var copy = await IndexFixture.CreateAsync(copyDir);
        Assert.False(copy.Db.WasRebuilt);
        Assert.NotNull(await copy.History.GetAsync("2026/10/15/praca_1403.md", Ct));
    }
}
