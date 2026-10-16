using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace WolfMark.Storage.Diagnostics;

/// <summary>
/// Minimal file sink: one file per day in <c>%LOCALAPPDATA%\WolfMark\logs</c>, written by a background
/// task so logging never blocks the caller (and never the UI thread). Keeps <see cref="RetentionDays"/> days.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetentionDays = 14;

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly TimeProvider _time;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Task _writer;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information, TimeProvider? time = null)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _time = time ?? TimeProvider.System;
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        Directory.CreateDirectory(_directory);
        CleanupOldFiles();
        var sb = new StringBuilder();
        while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            sb.Clear();
            while (_queue.Reader.TryRead(out var line))
            {
                sb.Append(line);
            }

            var file = Path.Combine(_directory, $"wolfmark-{_time.GetLocalNow():yyyyMMdd}.log");
            try
            {
                await File.AppendAllTextAsync(file, sb.ToString()).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void CleanupOldFiles()
    {
        try
        {
            var limit = _time.GetUtcNow().UtcDateTime.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "wolfmark-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Flushes pending lines (used on shutdown).</summary>
    public async Task FlushAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    public void Dispose() => _queue.Writer.TryComplete();

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var sb = new StringBuilder(256);
            sb.Append(provider._time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(Abbreviation(logLevel)).Append("] ")
              .Append(category).Append(": ")
              .Append(formatter(state, exception))
              .AppendLine();
            if (exception is not null)
            {
                sb.AppendLine(exception.ToString());
            }

            provider.Enqueue(sb.ToString());
        }

        private static string Abbreviation(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
