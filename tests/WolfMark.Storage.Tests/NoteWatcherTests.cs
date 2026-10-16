using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Events;
using WolfMark.Storage.Watching;

namespace WolfMark.Storage.Tests;

public sealed class NoteWatcherTests
{
    private static readonly DateTime At = new(2026, 10, 15, 14, 3, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<NoteEvent>> CollectAsync(EngineFixture f, TimeSpan quiet)
    {
        var events = new List<NoteEvent>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(quiet);
        try
        {
            while (await f.Events.WaitToReadAsync(cts.Token))
            {
                while (f.Events.TryRead(out var e))
                {
                    events.Add(e);
                }
            }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
        }

        return events;
    }

    [Fact]
    public async Task External_edit_raises_exactly_one_change()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, "praca", "v1", Ct);
        f.DrainEvents();

        await using var watcher = new NoteWatcher(f.Engine, f.Hub, TimeProvider.System, NullLogger<NoteWatcher>.Instance, TimeSpan.FromMilliseconds(150));
        watcher.Start();

        // An editor typically writes several times in quick succession.
        var full = f.Engine.GetFullPath(path);
        await File.WriteAllTextAsync(full, "v2", Ct);
        await File.AppendAllTextAsync(full, " more", Ct);
        await File.AppendAllTextAsync(full, " and more", Ct);

        var events = await CollectAsync(f, TimeSpan.FromSeconds(1.5));
        var change = Assert.IsType<NoteChanged>(Assert.Single(events));
        Assert.Equal(ChangeOrigin.External, change.Origin);
        Assert.Equal(path, change.Path);
    }

    [Fact]
    public async Task Own_save_raises_no_external_event()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, null, "v1", Ct);

        await using var watcher = new NoteWatcher(f.Engine, f.Hub, TimeProvider.System, NullLogger<NoteWatcher>.Instance, TimeSpan.FromMilliseconds(150));
        watcher.Start();
        f.DrainEvents();

        await f.Engine.SaveAsync(path, "v2", Ct);
        await f.Engine.SaveAsync(path, "v3", Ct);
        var renamed = await f.Engine.ChangeTagAsync(path, "praca", Ct);
        await f.Engine.DeleteAsync(renamed, Ct);

        var events = await CollectAsync(f, TimeSpan.FromSeconds(1.5));
        Assert.All(events, e => Assert.Equal(ChangeOrigin.Local, e.Origin));
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task External_create_and_delete()
    {
        using var f = new EngineFixture();
        await using var watcher = new NoteWatcher(f.Engine, f.Hub, TimeProvider.System, NullLogger<NoteWatcher>.Instance, TimeSpan.FromMilliseconds(150));
        Directory.CreateDirectory(f.Engine.GetDayDirectory(new DateOnly(2026, 10, 15)));
        watcher.Start();

        var full = Path.Combine(f.Root, "2026", "10", "15", "0800.md");
        await File.WriteAllTextAsync(full, "hello", Ct);
        var created = await CollectAsync(f, TimeSpan.FromSeconds(1));
        Assert.IsType<NoteCreated>(Assert.Single(created));

        File.Delete(full);
        var deleted = await CollectAsync(f, TimeSpan.FromSeconds(1));
        Assert.IsType<NoteDeleted>(Assert.Single(deleted));
    }

    [Fact]
    public async Task Tags_file_change_is_reported()
    {
        using var f = new EngineFixture();
        Directory.CreateDirectory(Path.Combine(f.Root, ".wolfmark"));
        await using var watcher = new NoteWatcher(f.Engine, f.Hub, TimeProvider.System, NullLogger<NoteWatcher>.Instance, TimeSpan.FromMilliseconds(150));
        watcher.Start();

        await File.WriteAllTextAsync(Path.Combine(f.Root, ".wolfmark", "tags.json"), "{}", Ct);
        var events = await CollectAsync(f, TimeSpan.FromSeconds(1));
        Assert.IsType<TagsChanged>(Assert.Single(events));
    }
}
