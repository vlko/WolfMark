using Microsoft.Extensions.Logging.Abstractions;
using Zapisnik.Index.Database;
using Zapisnik.Index.Indexing;
using Zapisnik.Index.Queries;
using Zapisnik.Storage;
using Zapisnik.Storage.Trash;

namespace Zapisnik.Index.Tests;

internal sealed class IndexFixture : IAsyncDisposable
{
    private IndexFixture(string baseDir)
    {
        BaseDir = baseDir;
        Root = Path.Combine(baseDir, "notes");
        Directory.CreateDirectory(Root);
        Hub = new NoteEventHub();
        Engine = new DirectoryEngine(Root, new FolderRecycleBin(Root), Hub, new SelfWriteRegistry(), NullLogger<DirectoryEngine>.Instance);
        Db = new IndexDatabase(Path.Combine(baseDir, "index.db"), NullLogger<IndexDatabase>.Instance);
        Indexer = new NoteIndexer(Db, Engine, NullLogger<NoteIndexer>.Instance);
        Calendar = new CalendarService(Db);
        History = new HistoryService(Db);
    }

    public string BaseDir { get; }

    public string Root { get; }

    public NoteEventHub Hub { get; }

    public DirectoryEngine Engine { get; }

    public IndexDatabase Db { get; private set; }

    public NoteIndexer Indexer { get; private set; }

    public CalendarService Calendar { get; private set; }

    public HistoryService History { get; private set; }

    public static async Task<IndexFixture> CreateAsync(string? baseDir = null)
    {
        var f = new IndexFixture(baseDir ?? Path.Combine(Path.GetTempPath(), "zapisnik-index-tests", Guid.NewGuid().ToString("N")));
        await f.Db.OpenAsync(TestContext.Current.CancellationToken);
        return f;
    }

    public async Task ReopenAsync()
    {
        await Db.DisposeAsync();
        Db = new IndexDatabase(Path.Combine(BaseDir, "index.db"), NullLogger<IndexDatabase>.Instance);
        await Db.OpenAsync(TestContext.Current.CancellationToken);
        Indexer = new NoteIndexer(Db, Engine, NullLogger<NoteIndexer>.Instance);
        Calendar = new CalendarService(Db);
        History = new HistoryService(Db);
    }

    public string Write(string relativePath, string content, DateTime? mtime = null)
    {
        var full = Engine.GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (mtime is { } m)
        {
            File.SetLastWriteTimeUtc(full, m);
        }

        return full;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        try
        {
            Directory.Delete(BaseDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
