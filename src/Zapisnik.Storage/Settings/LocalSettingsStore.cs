using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zapisnik.Core.Settings;

namespace Zapisnik.Storage.Settings;

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
                Current = await JsonSerializer.DeserializeAsync(stream, ZapisnikJsonContext.Default.LocalSettings, cancellationToken).ConfigureAwait(false)
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

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(Current, ZapisnikJsonContext.Default.LocalSettings);
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
