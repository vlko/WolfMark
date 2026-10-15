using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Zapisnik.Core.Events;
using Zapisnik.Core.Notes;
using Zapisnik.Core.Text;
using Zapisnik.Storage.Trash;

namespace Zapisnik.Storage;

/// <summary>
/// The notes directory is the source of truth (SPEC §1, §4.1). This class owns all file operations
/// on it; every operation is asynchronous and safe to call from any thread except the UI thread.
/// </summary>
public sealed partial class DirectoryEngine
{
    public const string InternalDirectoryName = ".zapisnik";
    public const string AssetsDirectoryName = "assets";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IRecycleBin _recycleBin;
    private readonly NoteEventHub _hub;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public DirectoryEngine(string root, IRecycleBin recycleBin, NoteEventHub hub, SelfWriteRegistry selfWrites, ILogger<DirectoryEngine> logger)
    {
        Root = Path.GetFullPath(root);
        _recycleBin = recycleBin;
        _hub = hub;
        SelfWrites = selfWrites;
        _logger = logger;
    }

    public string Root { get; }

    public SelfWriteRegistry SelfWrites { get; }

    public string GetFullPath(NotePath path) => GetFullPath(path.RelativePath);

    public string GetFullPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string GetRelativePath(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    public string GetDayDirectory(DateOnly day) => GetFullPath(NotePath.FormatDirectory(day));

    // ---------------------------------------------------------------- enumeration

    /// <summary>
    /// Enumerates all notes. Year directories are walked in parallel. Only directory metadata is read,
    /// so OneDrive placeholders are not downloaded.
    /// </summary>
    public async IAsyncEnumerable<NoteFile> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Root))
        {
            yield break;
        }

        var channel = Channel.CreateBounded<NoteFile>(new BoundedChannelOptions(1024) { SingleReader = true });
        var producer = Task.Run(
            async () =>
            {
                try
                {
                    var topLevel = new List<string>();
                    foreach (var entry in Directory.EnumerateFileSystemEntries(Root))
                    {
                        var name = Path.GetFileName(entry);
                        if (!IsIgnoredDirectoryName(name))
                        {
                            topLevel.Add(entry);
                        }
                    }

                    await Parallel.ForEachAsync(
                        topLevel,
                        new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) },
                        async (entry, ct) =>
                        {
                            if (Directory.Exists(entry))
                            {
                                foreach (var file in EnumerateDirectoryCore(entry, recursive: true))
                                {
                                    await channel.Writer.WriteAsync(file, ct).ConfigureAwait(false);
                                }
                            }
                            else if (TryCreateNoteFile(new FileInfo(entry)) is { } file)
                            {
                                await channel.Writer.WriteAsync(file, ct).ConfigureAwait(false);
                            }
                        }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            },
            cancellationToken);

        await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return file;
        }

        await producer.ConfigureAwait(false);
    }

    /// <summary>Enumerates notes of one month (partial resync, SPEC §4.3).</summary>
    public IAsyncEnumerable<NoteFile> EnumerateMonthAsync(int year, int month, CancellationToken cancellationToken = default) =>
        EnumerateDirectoryAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{year:D4}/{month:D2}"), recursive: true, cancellationToken);

    public async IAsyncEnumerable<NoteFile> EnumerateDirectoryAsync(string relativeDirectory, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var full = GetFullPath(relativeDirectory);
        if (!Directory.Exists(full))
        {
            yield break;
        }

        // Enumeration is synchronous metadata I/O; keep it off the caller's thread.
        var files = await Task.Run(() => EnumerateDirectoryCore(full, recursive).ToList(), cancellationToken).ConfigureAwait(false);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    public NoteFile? GetFile(NotePath path)
    {
        var info = new FileInfo(GetFullPath(path));
        return info.Exists ? TryCreateNoteFile(info) : null;
    }

    private IEnumerable<NoteFile> EnumerateDirectoryCore(string directory, bool recursive)
    {
        var root = Root;
        var enumerable = new FileSystemEnumerable<NoteFile?>(
            directory,
            (ref FileSystemEntry entry) =>
            {
                var relativeDir = Path.GetRelativePath(root, entry.Directory.ToString());
                var rel = relativeDir == "." ? entry.FileName.ToString() : relativeDir + "/" + entry.FileName.ToString();
                if (!NotePath.TryParse(rel, out var notePath))
                {
                    return null;
                }

                return new NoteFile(
                    notePath,
                    entry.ToFullPath(),
                    entry.LastWriteTimeUtc,
                    entry.CreationTimeUtc,
                    entry.Length,
                    CloudFileAttributes.IsPlaceholder(entry.Attributes));
            },
            new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && NotePath.IsCandidateFileName(entry.FileName.ToString()),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsIgnoredDirectoryName(entry.FileName),
        };

        foreach (var file in enumerable)
        {
            if (file is not null)
            {
                yield return file;
            }
        }
    }

    private NoteFile? TryCreateNoteFile(FileInfo info)
    {
        if (!NotePath.TryParse(GetRelativePath(info.FullName), out var path))
        {
            return null;
        }

        return new NoteFile(path, info.FullName, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Length, CloudFileAttributes.IsPlaceholder(info.Attributes));
    }

    private static bool IsIgnoredDirectoryName(ReadOnlySpan<char> name) =>
        name.IsEmpty || name[0] == '.' || name.Equals(AssetsDirectoryName, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- CRUD

    public async Task<string> ReadAsync(NotePath path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(GetFullPath(path), cancellationToken).ConfigureAwait(false);
        return Decode(bytes);
    }

    /// <summary>Decodes UTF-8 (with or without BOM). Line endings are preserved.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }

        return Utf8NoBom.GetString(bytes);
    }

    /// <summary>Path a new note would get right now (collisions are resolved again on create).</summary>
    public NotePath ProposeNewPath(DateTime createdAtLocal, string? tag) =>
        NotePath.Create(DateOnly.FromDateTime(createdAtLocal), NoteName.Create(tag, TimeOnly.FromDateTime(createdAtLocal)));

    /// <summary>Creates a note, resolving name collisions (seconds, then -2, -3 …).</summary>
    public async Task<NotePath> CreateAsync(DateTime createdAtLocal, string? tag, string content, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(createdAtLocal);
        var name = NoteName.Create(tag, TimeOnly.FromDateTime(createdAtLocal));
        var bytes = Utf8NoBom.GetBytes(content);
        var hash = ContentHash.Compute(bytes);

        await _createLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = await CreateWithUniqueNameAsync(day, name, createdAtLocal.Second, async candidate =>
            {
                SelfWrites.RecordWrite(candidate.RelativePath, hash);
                await AtomicFile.WriteAllBytesAsync(GetFullPath(candidate), bytes, overwrite: false, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            _hub.Publish(new NoteCreated(path, ChangeOrigin.Local));
            LogCreated(path.RelativePath);
            return path;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>Atomic save of an existing (or new) note.</summary>
    public async Task SaveAsync(NotePath path, string content, CancellationToken cancellationToken)
    {
        var bytes = Utf8NoBom.GetBytes(content);
        var full = GetFullPath(path);
        var existed = File.Exists(full);
        SelfWrites.RecordWrite(path.RelativePath, ContentHash.Compute(bytes));
        await AtomicFile.WriteAllBytesAsync(full, bytes, overwrite: true, cancellationToken).ConfigureAwait(false);
        _hub.Publish(existed ? new NoteChanged(path, ChangeOrigin.Local) : new NoteCreated(path, ChangeOrigin.Local));
    }

    /// <summary>Changing the tag renames the file; the creation time stays (SPEC §3.3).</summary>
    public async Task<NotePath> ChangeTagAsync(NotePath path, string? newTag, CancellationToken cancellationToken)
    {
        if (!path.IsStandard)
        {
            throw new InvalidOperationException($"Cannot change tag of non-standard note '{path}'.");
        }

        var target = path.Name!.WithTag(newTag);
        if (string.Equals(target.FileName, path.Name.FileName, StringComparison.Ordinal))
        {
            return path;
        }

        var source = GetFullPath(path);
        await _createLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var newPath = await CreateWithUniqueNameAsync(path.Date!.Value, target, path.Name.Time.Second, candidate =>
            {
                SelfWrites.ExpectRemoval(path.RelativePath);
                SelfWrites.ExpectRemoval(candidate.RelativePath);
                var targetFull = GetFullPath(candidate);
                if (string.Equals(source, targetFull, StringComparison.OrdinalIgnoreCase))
                {
                    // Case-only rename on a case-insensitive file system.
                    var temp = source + ".rename" + AtomicFile.TempSuffix;
                    File.Move(source, temp);
                    File.Move(temp, targetFull);
                }
                else
                {
                    File.Move(source, targetFull, overwrite: false);
                }

                return Task.CompletedTask;
            }, ignoreExisting: source).ConfigureAwait(false);

            SelfWrites.Forget(path.RelativePath);
            _hub.Publish(new NoteRenamed(path, newPath, ChangeOrigin.Local));
            return newPath;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>Moves the note to the Recycle Bin – never deletes permanently.</summary>
    public async Task DeleteAsync(NotePath path, CancellationToken cancellationToken)
    {
        SelfWrites.ExpectRemoval(path.RelativePath);
        await _recycleBin.MoveToRecycleBinAsync(GetFullPath(path), cancellationToken).ConfigureAwait(false);
        SelfWrites.Forget(path.RelativePath);
        _hub.Publish(new NoteDeleted(path, ChangeOrigin.Local));
    }

    /// <summary>
    /// Stores a pasted image in <c>YYYY/MM/DD/assets/HHmm-NN.ext</c> and returns the link relative to the note.
    /// </summary>
    public async Task<string> SaveAssetAsync(DateOnly day, DateTime nowLocal, string extension, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(GetDayDirectory(day), AssetsDirectoryName);
        Directory.CreateDirectory(dir);
        extension = extension.StartsWith('.') ? extension : "." + extension;
        for (var n = 1; n < 1000; n++)
        {
            var fileName = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{nowLocal:HHmm}-{n:D2}{extension}");
            var full = Path.Combine(dir, fileName);
            if (File.Exists(full))
            {
                continue;
            }

            try
            {
                await AtomicFile.WriteAllBytesAsync(full, data, overwrite: false, cancellationToken).ConfigureAwait(false);
                return AssetsDirectoryName + "/" + fileName;
            }
            catch (IOException) when (File.Exists(full))
            {
                // Lost a race – try the next number.
            }
        }

        throw new IOException("Too many assets for one minute.");
    }

    private async Task<NotePath> CreateWithUniqueNameAsync(DateOnly day, NoteName name, int seconds, Func<NotePath, Task> create, string? ignoreExisting = null)
    {
        var dir = GetDayDirectory(day);
        Directory.CreateDirectory(dir);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (ignoreExisting is null || !string.Equals(f, ignoreExisting, StringComparison.OrdinalIgnoreCase))
            {
                existing.Add(Path.GetFileName(f));
            }
        }

        var candidate = name;
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            if (!existing.Contains(candidate.FileName))
            {
                var path = NotePath.Create(day, candidate);
                try
                {
                    await create(path).ConfigureAwait(false);
                    return path;
                }
                catch (IOException) when (File.Exists(GetFullPath(path)))
                {
                    // Created concurrently by someone else (e.g. OneDrive) – try the next name.
                    existing.Add(candidate.FileName);
                }
            }

            candidate = candidate.NextCollisionCandidate(seconds);
        }

        throw new IOException($"Could not find a free file name for {name} in {dir}.");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created note {Path}")]
    private partial void LogCreated(string path);
}
