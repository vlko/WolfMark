using System.Diagnostics;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Index.Indexing;
using WolfMark.Index.Queries;

namespace WolfMark.Index.Tests;

public sealed class IndexerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Second_scan_reindexes_nothing_and_one_change_reindexes_one()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        f.Write("2026/10/15/0927.md", "B");
        f.Write("2026/10/14/ClaudeCode_1120.md", "C");

        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Equal(3, f.Indexer.IndexedCount);

        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Equal(3, f.Indexer.IndexedCount);

        f.Write("2026/10/15/0927.md", "B changed", DateTime.UtcNow.AddMinutes(1));
        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Equal(4, f.Indexer.IndexedCount);
    }

    [Fact]
    public async Task Reopening_database_does_not_reindex()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# A");
        await f.Indexer.FullScanAsync(null, Ct);

        await f.ReopenAsync();
        Assert.False(f.Db.WasRebuilt);
        await f.Indexer.FullScanAsync(null, Ct);
        Assert.Equal(0, f.Indexer.IndexedCount);
    }

    [Fact]
    public async Task Extracts_metadata_and_tags()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# Hotfix pre export\nDetail #finstat\n- [ ] a\n- [x] b");
        await f.Indexer.FullScanAsync(null, Ct);

        var note = await f.History.GetAsync("2026/10/15/praca_1403.md", Ct);
        Assert.NotNull(note);
        Assert.Equal("Hotfix pre export", note.Title);
        Assert.Equal("praca", note.PrimaryTag);
        Assert.Equal(new DateTime(2026, 10, 15, 14, 3, 0), note.CreatedAt);
        Assert.Equal(1, note.OpenTasks);
        Assert.Equal(1, note.DoneTasks);

        var tags = await f.History.GetAllTagsAsync(Ct);
        Assert.Equal(["finstat", "praca"], tags.Select(t => t.Tag).Order());

        var byInlineTag = await f.History.GetPageAsync(new HistoryQuery(Tag: "FINSTAT"), Ct);
        Assert.Single(byInlineTag.Items);
    }

    [Fact]
    public async Task Calendar_days_aggregate_tags()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_0900.md", "a");
        f.Write("2026/10/15/praca_1000.md", "- [ ] t");
        f.Write("2026/10/15/rodina_1100.md", "c");
        f.Write("2026/10/15/1200.md", "d");
        f.Write("2026/10/16/praca_0900.md", "e");
        await f.Indexer.FullScanAsync(null, Ct);

        var week = await f.Calendar.GetWeekAsync(new DateOnly(2026, 10, 15), Ct);
        var day = week[new DateOnly(2026, 10, 15)];
        Assert.Equal(4, day.NoteCount);
        Assert.Equal(("praca", 2), (day.Tags[0].Tag, day.Tags[0].Count));
        Assert.Equal("praca", day.DominantTag);
        Assert.Equal(1, day.OpenTasks);
        Assert.Contains(day.Tags, t => t is { Tag: "", Count: 1 });
        Assert.Equal(2, week.Count);

        var highlight = await f.Calendar.GetWeekHighlightAsync(new DateOnly(2026, 10, 12), Ct);
        Assert.Equal("praca", highlight!.Tag);
        Assert.Equal(3, highlight.Count);
    }

    [Fact]
    public async Task Events_update_index_and_calendar_within_a_second()
    {
        await using var f = await IndexFixture.CreateAsync();
        await f.Indexer.FullScanAsync(null, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var changed = new TaskCompletionSource<IndexChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Indexer.Changed += (_, e) => changed.TrySetResult(e);
        var loop = f.Indexer.RunAsync(f.Hub.Subscribe(), cts.Token);

        var sw = Stopwatch.StartNew();
        var path = await f.Engine.CreateAsync(new DateTime(2026, 10, 15, 8, 0, 0), "praca", "# Nová", Ct);
        var args = await changed.Task.WaitAsync(TimeSpan.FromSeconds(1), Ct);
        sw.Stop();

        Assert.Contains(new DateOnly(2026, 10, 15), args.Days);
        var days = await f.Calendar.GetDaysAsync(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), Ct);
        Assert.Equal(1, days[new DateOnly(2026, 10, 15)].NoteCount);

        // Rename (tag change) moves the note; delete removes it.
        var renamed = await f.Engine.ChangeTagAsync(path, "rodina", Ct);
        await f.Engine.DeleteAsync(renamed, Ct);
        await WaitUntilAsync(async () => (await f.Calendar.GetDaysAsync(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), Ct)).Count == 0);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task Manually_added_file_appears_after_month_resync()
    {
        await using var f = await IndexFixture.CreateAsync();
        await f.Indexer.FullScanAsync(null, Ct);
        f.Write("2026/10/03/0800.md", "added while app was closed");
        await f.Indexer.ScanMonthAsync(2026, 10, Ct);
        var month = await f.Calendar.GetMonthAsync(2026, 10, Ct);
        Assert.True(month.ContainsKey(new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public async Task History_pages_with_keyset_cursor()
    {
        await using var f = await IndexFixture.CreateAsync();
        for (var i = 0; i < 25; i++)
        {
            f.Write($"2026/10/{1 + (i / 10):D2}/{i % 10:D2}{i:D2}.md", $"note {i}");
        }

        await f.Indexer.FullScanAsync(null, Ct);
        var all = new List<NoteSummary>();
        HistoryCursor? cursor = null;
        do
        {
            var page = await f.History.GetPageAsync(new HistoryQuery(After: cursor, Limit: 10), Ct);
            all.AddRange(page.Items);
            cursor = page.Next;
        }
        while (cursor is not null);

        Assert.Equal(25, all.Count);
        Assert.Equal(all.OrderByDescending(n => n.ModifiedUtcTicks).ThenByDescending(n => n.Id).Select(n => n.Id), all.Select(n => n.Id));
        Assert.Equal(25, all.Select(n => n.Id).Distinct().Count());
    }

    [Fact]
    public async Task History_is_ordered_by_last_modification()
    {
        await using var f = await IndexFixture.CreateAsync();
        var now = DateTime.UtcNow;
        f.Write("2026/10/01/0800.md", "old note", now.AddDays(-14));
        f.Write("2026/10/15/0900.md", "new note", now.AddDays(-1));
        f.Write("2026/10/10/1000.md", "middle note", now.AddDays(-5));
        await f.Indexer.FullScanAsync(null, Ct);

        var before = await f.History.GetPageAsync(new HistoryQuery(), Ct);
        Assert.Equal(["2026/10/15/0900.md", "2026/10/10/1000.md", "2026/10/01/0800.md"], before.Items.Select(n => n.RelativePath));

        // Editing the oldest note moves it to the top; created time stays.
        f.Write("2026/10/01/0800.md", "old note, edited today", now);
        await f.Indexer.FullScanAsync(null, Ct);
        var after = await f.History.GetPageAsync(new HistoryQuery(), Ct);
        Assert.Equal("2026/10/01/0800.md", after.Items[0].RelativePath);
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), after.Items[0].CreatedAt);
        Assert.Equal(now.ToLocalTime(), after.Items[0].ModifiedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Conflict_copies_are_flagged()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "mine");
        f.Write("2026/10/15/praca_1403-LAPTOP.md", "theirs");
        await f.Indexer.FullScanAsync(null, Ct);
        var conflict = Assert.Single(await f.History.GetConflictsAsync(Ct));
        Assert.Equal("2026/10/15/praca_1403-LAPTOP.md", conflict.RelativePath);
    }

    [Fact]
    public async Task Non_standard_file_uses_file_time()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/nakup.md", "mlieko");
        await f.Indexer.FullScanAsync(null, Ct);
        var note = await f.History.GetAsync("2026/10/15/nakup.md", Ct);
        Assert.False(note!.IsStandard);
        Assert.Equal(new DateOnly(2026, 10, 15), note.Day);
    }

    [Fact]
    public async Task Parallel_writes_are_serialized()
    {
        await using var f = await IndexFixture.CreateAsync();
        var tasks = Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => f.Indexer.ApplyAsync([WriteNote(f, i)], [], Ct), Ct));
        await Task.WhenAll(tasks);
        var page = await f.History.GetPageAsync(new HistoryQuery(Limit: 500), Ct);
        Assert.Equal(200, page.Items.Count);

        static string WriteNote(IndexFixture f, int i)
        {
            var rel = $"2026/09/{1 + (i % 28):D2}/{i % 24:D2}{i % 60:D2}{i / 60:D2}.md";
            f.Write(rel, $"n{i}");
            return NotePath.Parse(rel).RelativePath;
        }
    }

    [Fact]
    public async Task Full_reindex_of_10000_notes_is_fast()
    {
        await using var f = await IndexFixture.CreateAsync();
        for (var d = 0; d < 100; d++)
        {
            var day = new DateOnly(2025, 1, 1).AddDays(d);
            for (var i = 0; i < 100; i++)
            {
                f.Write($"{NotePath.FormatDirectory(day)}/tag{i % 7}_{i / 60:D2}{i % 60:D2}.md", $"# Poznámka {d}-{i}\nText o projekte číslo {i} a deploymente.");
            }
        }

        var sw = Stopwatch.StartNew();
        await f.Indexer.FullScanAsync(null, Ct);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Reindex took {sw.Elapsed}");

        var search = new SearchService(f.Db);
        await search.SearchAsync("poznamka", 20, Ct); // warm-up
        var sw2 = Stopwatch.StartNew();
        var hits = await search.SearchAsync("poznamka deploymente", 20, Ct);
        sw2.Stop();
        Assert.Equal(20, hits.Count);
        Assert.True(sw2.ElapsedMilliseconds < 50, $"FTS took {sw2.ElapsedMilliseconds} ms");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "condition not met in time");
            await Task.Delay(20, Ct);
        }
    }
}
