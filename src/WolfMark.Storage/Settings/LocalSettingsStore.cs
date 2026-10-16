using System.Text.Json;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Settings;

namespace WolfMark.Storage.Settings;

/// <summary>Loads/saves <see cref="LocalSettings"/> atomically (STJ source generation).</summary>
public sealed partial class LocalSettingsStore(LocalPaths paths, ILogger<LocalSettingsStore> logger)
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public LocalSettings Current { get; private set; } = new();

    public async Task<LocalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
            {
                await using var stream = File.OpenRead(paths.SettingsFile);
                Current = await JsonSerializer.DeserializeAsync(stream, WolfMarkJsonContext.Default.LocalSettings, cancellationToken).ConfigureAwait(false)
                          ?? new LocalSettings();
            }
        }
        catch (JsonException ex)
        {
            LogInvalid(ex, paths.SettingsFile);
            Current = new LocalSettings();
        }

        return Current;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => SaveSnapshotAsync(CreateSnapshot(), cancellationToken);

    /// <summary>
    /// Serialises <see cref="Current"/> on the calling thread. The UI owns the settings object, so it takes the
    /// snapshot itself and only the file write moves to a background thread (no data race).
    /// </summary>
    public string CreateSnapshot() => JsonSerializer.Serialize(Current, WolfMarkJsonContext.Default.LocalSettings);

    public async Task SaveSnapshotAsync(string json, CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAllTextAsync(paths.SettingsFile, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Resolved notes root: configured or default.</summary>
    public string ResolveRoot() => string.IsNullOrWhiteSpace(Current.RootDirectory)
        ? RootDirectoryResolver.ResolveDefault()
        : Current.RootDirectory;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings file {Path} is invalid – using defaults")]
    private partial void LogInvalid(Exception ex, string path);
}
