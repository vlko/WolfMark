using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WolfMark.Index.Database;

/// <summary>
/// The SQLite index (SPEC §4.2): WAL mode, exactly one writer connection serviced by a dedicated
/// background loop (CLAUDE.md rule 6), separate pooled read connections. Corruption or schema
/// mismatch → the database is rebuilt, since it is only a cache of the notes directory.
/// </summary>
public sealed partial class IndexDatabase : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Channel<WorkItem> _work = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentBag<SqliteConnection> _readers = [];
    private readonly string _connectionString;
    private SqliteConnection? _writer;
    private Task? _writerLoop;
    private string? _vectorExtensionPath;

    public IndexDatabase(string path, ILogger<IndexDatabase> logger)
    {
        Path = path;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString();
    }

    public string Path { get; }

    /// <summary>True when sqlite-vec loaded (not available e.g. on win-arm64 yet).</summary>
    public bool VectorsAvailable => _vectorExtensionPath is not null;

    /// <summary>True when <see cref="OpenAsync"/> had to create or rebuild the database (a full scan is needed).</summary>
    public bool WasRebuilt { get; private set; }

    /// <summary>Raised (on the writer thread) after the database was wiped and recreated.</summary>
    public event EventHandler? Reset;

    public Task OpenAsync(CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            _vectorExtensionPath = FindVectorExtension();

            try
            {
                _writer = OpenConnection();
                if (!IsHealthy(_writer))
                {
                    LogRebuilding("integrity check or schema version failed");
                    RecreateFile();
                }
            }
            catch (SqliteException ex)
            {
                LogOpenFailed(ex);
                RecreateFile();
            }

            EnsureSchema(_writer!);
            _writerLoop = Task.Run(WriterLoopAsync, CancellationToken.None);
        },
        cancellationToken);

    /// <summary>Runs <paramref name="work"/> on the single writer connection.</summary>
    public Task<T> WriteAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken = default)
    {
        var item = new WorkItem(c => work(c), cancellationToken);
        ObjectDisposedException.ThrowIf(!_work.Writer.TryWrite(item), this);

        return Cast<T>(item.Completion.Task);
    }

    public Task WriteAsync(Action<SqliteConnection> work, CancellationToken cancellationToken = default) =>
        WriteAsync<object?>(c =>
        {
            work(c);
            return null;
        }, cancellationToken);

    /// <summary>Runs a read on a pooled connection, off the calling thread.</summary>
    public Task<T> ReadAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_readers.TryTake(out var connection))
                {
                    connection = OpenConnection();
                }

                try
                {
                    var result = work(connection);
                    _readers.Add(connection);
                    return result;
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            },
            cancellationToken);

    /// <summary>Drops and recreates all tables (index rebuild, M2-07). Callers must rescan afterwards.</summary>
    public Task ResetAsync(CancellationToken cancellationToken) => WriteAsync(
        c =>
        {
            DropAllTables(c);
            EnsureSchema(c);
            Reset?.Invoke(this, EventArgs.Empty);
        },
        cancellationToken);

    /// <summary>Drops every table, including those created by other components (e.g. sync state).</summary>
    private static void DropAllTables(SqliteConnection c)
    {
        var tables = new List<(string Name, bool IsVirtual)>();
        using (var cmd = c.Command("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                tables.Add((r.GetString(0), r.GetStringOrNull(1)?.StartsWith("CREATE VIRTUAL", StringComparison.OrdinalIgnoreCase) == true));
            }
        }

        // Virtual tables first: dropping them removes their shadow tables.
        foreach (var (name, _) in tables.OrderByDescending(t => t.IsVirtual))
        {
            var exists = c.Scalar($"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{name.Replace("'", "''")}';") == "1";
            if (exists)
            {
                c.Execute($"DROP TABLE \"{name.Replace("\"", "\"\"")}\";");
            }
        }
    }

    private static async Task<T> Cast<T>(Task<object?> task) => (T)(await task.ConfigureAwait(false))!;

    /// <summary>Only one instance runs, so the writer connection is never used concurrently.</summary>
    private async Task WriterLoopAsync()
    {
        var reader = _work.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                try
                {
                    item.Completion.TrySetResult(item.Work(_writer!));
                }
                catch (SqliteException ex) when (IsCorruption(ex))
                {
                    LogCorrupted(ex);
                    try
                    {
                        RecreateFile();
                        EnsureSchema(_writer!);
                        Reset?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception inner)
                    {
                        LogOpenFailed(inner);
                    }

                    item.Completion.TrySetException(ex);
                }
                catch (Exception ex)
                {
                    item.Completion.TrySetException(ex);
                }
            }
        }
    }

    private static bool IsCorruption(SqliteException ex) =>
        ex.SqliteErrorCode is 11 /* CORRUPT */ or 26 /* NOTADB */ or 10 /* IOERR */ or 8 /* READONLY (file deleted) */;

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            connection.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        }
        catch
        {
            // A corrupt file fails on the first statement; release the handle or Windows cannot delete it.
            connection.Dispose();
            throw;
        }

        if (_vectorExtensionPath is not null)
        {
            try
            {
                connection.EnableExtensions(true);
                connection.LoadExtension(_vectorExtensionPath);
                connection.EnableExtensions(false);
            }
            catch (SqliteException ex)
            {
                LogVectorUnavailable(ex.Message);
                _vectorExtensionPath = null;
            }
        }

        return connection;
    }

    private bool IsHealthy(SqliteConnection connection)
    {
        var integrity = connection.Scalar("PRAGMA quick_check;");
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasMeta = connection.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta';") == "1";
        if (!hasMeta)
        {
            WasRebuilt = true;
            return true; // fresh database, schema will be created
        }

        return connection.Scalar("SELECT value FROM meta WHERE key = 'schema_version';") == Schema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        var hasNotes = connection.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'notes';") == "1";
        if (hasNotes)
        {
            return;
        }

        DropAllTables(connection);
        using var tx = connection.BeginTransaction();
        connection.Execute(Schema.Create, tx);
        using (var cmd = connection.Command("INSERT INTO meta(key, value) VALUES ('schema_version', $v);", tx))
        {
            cmd.With("$v", Schema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)).ExecuteNonQuery();
        }

        tx.Commit();
        WasRebuilt = true;
    }

    private void RecreateFile()
    {
        _writer?.Dispose();
        _writer = null;
        while (_readers.TryTake(out var r))
        {
            r.Dispose();
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = Path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        _writer = OpenConnection();
        WasRebuilt = true;
    }

    /// <summary>Locates the vec0 native library next to the app (or in runtimes/&lt;rid&gt;/native for dev builds).</summary>
    private static string? FindVectorExtension()
    {
        var fileName = OperatingSystem.IsWindows() ? "vec0.dll" : OperatingSystem.IsMacOS() ? "vec0.dylib" : "vec0.so";
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            System.IO.Path.Combine(baseDir, fileName),
            System.IO.Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
            System.IO.Path.Combine(baseDir, "runtimes", RidFallback(), "native", fileName),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string RidFallback()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        return os + "-" + arch;
    }

    public async ValueTask DisposeAsync()
    {
        _work.Writer.TryComplete();
        if (_writerLoop is not null)
        {
            await _writerLoop.ConfigureAwait(false);
        }

        _writer?.Dispose();
        while (_readers.TryTake(out var r))
        {
            r.Dispose();
        }

        SqliteConnection.ClearAllPools();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuilding index: {Reason}")]
    private partial void LogRebuilding(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Index database could not be opened – recreating")]
    private partial void LogOpenFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Index database corrupted – recreating")]
    private partial void LogCorrupted(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "sqlite-vec unavailable, semantic search disabled: {Reason}")]
    private partial void LogVectorUnavailable(string reason);

    private sealed record WorkItem(Func<SqliteConnection, object?> Work, CancellationToken CancellationToken)
    {
        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
