using System.Text.Json;
using WolfMark.Core.Settings;
using WolfMark.Storage.Trash;

namespace WolfMark.Storage.Settings;

/// <summary>
/// Switches between standard and portable mode (ADR 0009) by copying the data; the app restarts afterwards
/// because every path is fixed for the lifetime of the process. The marker is the switch: it is written last
/// when entering and removed last when leaving, so an interrupted copy changes nothing.
/// </summary>
public static class PortableMigrator
{
    /// <param name="FilesCopied">Notes and attachments copied (files already identical in the target are not counted).</param>
    /// <param name="CleanupError">
    /// The data was copied and the mode switched, but the original notes could not be sent to the recycle bin.
    /// </param>
    public sealed record Result(int FilesCopied, Exception? CleanupError);

    /// <summary>The live data of the running app.</summary>
    /// <param name="BackupIndex">Writes a consistent copy of the open index to the given path.</param>
    public sealed record Source(LocalPaths Paths, string NotesRoot, string SettingsJson, Func<string, CancellationToken, Task> BackupIndex);

    /// <summary>
    /// Copies notes, settings, index and models into <paramref name="dataDirectory"/> and writes the marker next to
    /// the app. With <paramref name="recycleOriginalNotes"/> the original notes folder goes to that recycle bin
    /// afterwards; the user-profile data folder stays (the open index cannot be moved; it is only a cache).
    /// </summary>
    /// <exception cref="IOException">The data folder is not empty, or the copy failed (nothing switched).</exception>
    public static async Task<Result> EnableAsync(
        string appDirectory,
        string dataDirectory,
        Source source,
        IRecycleBin? recycleOriginalNotes,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        dataDirectory = Path.GetFullPath(dataDirectory);
        if (Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any())
        {
            throw new IOException($"The portable folder '{dataDirectory}' is not empty. Move it away or delete it first.");
        }

        var target = LocalPaths.ForPortable(dataDirectory);
        var notes = Path.Combine(dataDirectory, PortableMode.NotesFolderName);
        NotesMover.Result copied;
        try
        {
            copied = await NotesMover.CopyAsync(source.NotesRoot, notes, progress, replaceDifferent: null, ct).ConfigureAwait(false);
            Directory.CreateDirectory(target.BaseDirectory);
            await source.BackupIndex(target.IndexDatabase, ct).ConfigureAwait(false);
            if (Directory.Exists(source.Paths.ModelsDirectory))
            {
                await NotesMover.CopyAsync(source.Paths.ModelsDirectory, target.ModelsDirectory, null, replaceDifferent: null, ct).ConfigureAwait(false);
            }

            await WriteSettingsAsync(source.SettingsJson, target.SettingsFile, PortableMode.NotesFolderName, ct).ConfigureAwait(false);
            await PortableMode.WriteMarkerAsync(appDirectory, dataDirectory, ct).ConfigureAwait(false);
        }
        catch
        {
            // The folder was empty before and holds only the copies just made – the originals are untouched.
            TryDelete(dataDirectory);
            throw;
        }

        Exception? cleanupError = null;
        if (recycleOriginalNotes is not null && Directory.Exists(source.NotesRoot))
        {
            try
            {
                await recycleOriginalNotes.MoveToRecycleBinAsync(Path.GetFullPath(source.NotesRoot), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupError = ex;
            }
        }

        return new Result(copied.FilesCopied, cleanupError);
    }

    /// <summary>
    /// Copies the portable notes, settings, index and models back to the user profile and removes the marker. The
    /// portable folder stays (on a USB drive there is no recycle bin – a move would delete permanently).
    /// A note in <paramref name="targetNotesRoot"/> that differs from the portable one goes to
    /// <paramref name="replacedNotes"/> and the portable version takes its place.
    /// </summary>
    public static async Task<Result> DisableAsync(
        string appDirectory,
        Source source,
        LocalPaths target,
        string targetNotesRoot,
        IRecycleBin replacedNotes,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var copied = await NotesMover.CopyAsync(source.NotesRoot, targetNotesRoot, progress, replacedNotes, ct).ConfigureAwait(false);
        Directory.CreateDirectory(target.BaseDirectory);
        await source.BackupIndex(target.IndexDatabase, ct).ConfigureAwait(false);
        if (Directory.Exists(source.Paths.ModelsDirectory))
        {
            await NotesMover.CopyAsync(source.Paths.ModelsDirectory, target.ModelsDirectory, null, replaceDifferent: null, ct).ConfigureAwait(false);
        }

        var stored = PathScope.IsSame(targetNotesRoot, RootDirectoryResolver.ResolveDefault()) ? null : targetNotesRoot;
        await WriteSettingsAsync(source.SettingsJson, target.SettingsFile, stored, ct).ConfigureAwait(false);
        File.Delete(PortableMode.MarkerPath(appDirectory)); // the switch itself – a config file, not a note
        return new Result(copied.FilesCopied, null);
    }

    /// <summary>The notes root the user had before portable mode (from the user-profile settings), or the default.</summary>
    public static string StandardNotesRoot(LocalPaths standard)
    {
        try
        {
            if (File.Exists(standard.SettingsFile)
                && JsonSerializer.Deserialize(File.ReadAllBytes(standard.SettingsFile), WolfMarkJsonContext.Default.LocalSettings) is { RootDirectory: { } root }
                && Path.IsPathFullyQualified(root))
            {
                return root;
            }
        }
        catch (JsonException)
        {
        }

        return RootDirectoryResolver.ResolveDefault();
    }

    private static Task WriteSettingsAsync(string settingsJson, string path, string? rootDirectory, CancellationToken ct)
    {
        var settings = JsonSerializer.Deserialize(settingsJson, WolfMarkJsonContext.Default.LocalSettings) ?? new LocalSettings();
        settings.RootDirectory = rootDirectory;
        return AtomicFile.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, WolfMarkJsonContext.Default.LocalSettings), ct);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a partial copy stays; the next attempt reports the folder as not empty
        }
    }
}
