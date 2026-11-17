using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WolfMark.Storage;
using WolfMark.Storage.Trash;

namespace WolfMark.Sync.Share.Tests;

/// <summary>A notes folder in the temp directory and a clock that stands at 17 Nov 2026, 14:05 local time.</summary>
internal sealed class ShareFixture : IDisposable
{
    public static readonly DateTime NowLocal = new(2026, 11, 17, 14, 5, 0, DateTimeKind.Unspecified);

    public ShareFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "wolfmark-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Time = new FakeTimeProvider(new DateTimeOffset(NowLocal, TimeSpan.FromHours(1)));
        Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test"));
        Engine = new DirectoryEngine(Root, new FolderRecycleBin(Root), new NoteEventHub(), new SelfWriteRegistry(), NullLogger<DirectoryEngine>.Instance);
        Receiver = new ShareReceiver(Engine, Time, NullLogger<ShareReceiver>.Instance);
    }

    public string Root { get; }

    public FakeTimeProvider Time { get; }

    public DirectoryEngine Engine { get; }

    public ShareReceiver Receiver { get; }

    public static byte[] Key { get; } = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    public ShareMessage Message(string markdown, Guid? id = null, DateTime? created = null, params ShareAttachment[] attachments) =>
        new(id ?? Guid.NewGuid(), Time.GetUtcNow(), created ?? new DateTime(2026, 11, 17, 14, 3, 27, DateTimeKind.Unspecified), markdown, attachments);

    public string[] Notes() => [.. Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
