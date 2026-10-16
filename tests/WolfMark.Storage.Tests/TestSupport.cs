using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Events;
using WolfMark.Storage.Trash;

namespace WolfMark.Storage.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wolfmark-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class EngineFixture : IDisposable
{
    private readonly TempDirectory _dir = new();

    public EngineFixture()
    {
        Hub = new NoteEventHub();
        Events = Hub.Subscribe();
        SelfWrites = new SelfWriteRegistry();
        RecycleBin = new FolderRecycleBin(Root);
        Engine = new DirectoryEngine(Root, RecycleBin, Hub, SelfWrites, NullLogger<DirectoryEngine>.Instance);
    }

    public string Root => _dir.Path;

    public NoteEventHub Hub { get; }

    public ChannelReader<NoteEvent> Events { get; }

    public SelfWriteRegistry SelfWrites { get; }

    public FolderRecycleBin RecycleBin { get; }

    public DirectoryEngine Engine { get; }

    public List<NoteEvent> DrainEvents()
    {
        var list = new List<NoteEvent>();
        while (Events.TryRead(out var e))
        {
            list.Add(e);
        }

        return list;
    }

    public void Dispose() => _dir.Dispose();
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception), exception));
}
