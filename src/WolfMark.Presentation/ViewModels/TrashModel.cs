using System.Globalization;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Notes;
using WolfMark.Core.Settings;
using WolfMark.Storage.Trash;

namespace WolfMark.App.ViewModels;

/// <summary>A note in the list of Settings → Trash: what it was and when it went.</summary>
internal sealed record TrashRow(TrashedNote Note, string Title, string Details);

/// <summary>
/// Settings → Trash, shared by both apps (ADR 0016): the deleted notes, restoring one to where it was, removing one
/// or all of them, and how long they are kept. The views only lay it out.
/// </summary>
internal sealed class TrashModel(MainViewModel vm)
{
    private AppHost Host => vm.Host;

    public IReadOnlyList<TrashRow> Items { get; private set; } = [];

    /// <summary>"3 notes in the trash, 12 kB" or that it is empty.</summary>
    public string Summary => Items.Count == 0
        ? Strings.Get(R.Trash_Empty)
        : Strings.Format(R.Trash_Summary, Strings.Notes(Items.Count), FormatSize(Items.Sum(i => i.Note.Size)));

    /// <summary>What removing from the trash means here: the system's recycle bin, or gone for good.</summary>
    public string RemovalHint => Strings.Get(Host.SystemBin is not null ? R.Trash_ToSystemBin : R.Trash_ForGood);

    /// <summary>Removing is final here – the views ask before they do it.</summary>
    public bool RemovesForGood => Host.SystemBin is null;

    public static IReadOnlyList<int> RetentionChoices => LocalSettings.TrashRetentionChoices;

    public static string RetentionText(int days) =>
        days <= 0 ? Strings.Get(R.Trash_RetentionNever) : Strings.Format(R.Trash_RetentionDays, days);

    /// <summary>Index of the saved retention among <see cref="RetentionChoices"/>; a value written by hand counts as the next longer one.</summary>
    public int RetentionIndex
    {
        get
        {
            var days = Host.Settings.Current.TrashRetentionDays;
            for (var i = 0; i < RetentionChoices.Count; i++)
            {
                if (days <= RetentionChoices[i])
                {
                    return days <= 0 ? 0 : i;
                }
            }

            return RetentionChoices.Count - 1;
        }
    }

    public async Task SetRetentionAsync(int index)
    {
        Host.Settings.Current.TrashRetentionDays = RetentionChoices[Math.Clamp(index, 0, RetentionChoices.Count - 1)];
        await Host.SaveSettingsAsync();
    }

    public async Task LoadAsync()
    {
        var notes = await Task.Run(Host.Trash.List);
        Items = [.. notes.Select(ToRow)];
    }

    /// <summary>Back to where it was (or the next free name there); the note opens in the main window.</summary>
    /// <returns>The message to show: where it is now, or why it failed.</returns>
    public async Task<string> RestoreAsync(TrashRow row)
    {
        try
        {
            var restored = await Task.Run(async () =>
            {
                var path = await Host.Engine.RestoreAsync(row.Note, CancellationToken.None);
                Host.Trash.RemoveEmptyFolders();
                return path; // the engine announced the note: the index and the history pick it up from there
            });
            await LoadAsync();
            await vm.OpenNoteAsync(restored.RelativePath);
            return Strings.Format(R.Trash_Restored, restored.RelativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await LoadAsync();
            return Strings.Format(R.Trash_Failed, ex.Message);
        }
    }

    /// <returns>Null when done, otherwise why it failed.</returns>
    public async Task<string?> RemoveAsync(TrashRow row)
    {
        try
        {
            await Task.Run(async () =>
            {
                await Host.Trash.DeleteAsync(row.Note, Host.SystemBin, CancellationToken.None);
                Host.Trash.RemoveEmptyFolders();
            });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Strings.Format(R.Trash_Failed, ex.Message);
        }
        finally
        {
            await LoadAsync();
        }
    }

    /// <returns>Null when done, otherwise why it failed.</returns>
    public async Task<string?> EmptyAsync()
    {
        try
        {
            await Task.Run(() => Host.Trash.EmptyAsync(null, Host.SystemBin, CancellationToken.None));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Strings.Format(R.Trash_Failed, ex.Message);
        }
        finally
        {
            await LoadAsync();
        }
    }

    private static TrashRow ToRow(TrashedNote note)
    {
        var c = CultureInfo.CurrentCulture;
        var deleted = note.DeletedAtUtc.ToLocalTime();
        var when = deleted.ToString("d", c) + " " + deleted.ToString("t", c);
        string what;
        if (NotePath.TryParse(note.OriginalRelativePath, out var path) && path.IsStandard)
        {
            var created = path.CreatedAt!.Value;
            what = created.ToString("d", c) + " " + created.ToString("t", c) + (path.Tag is { } tag ? " · " + tag : string.Empty);
        }
        else
        {
            what = note.OriginalRelativePath;
        }

        return new TrashRow(note, note.Title.Length > 0 ? note.Title : Strings.Get(R.Trash_Untitled), Strings.Format(R.Trash_Details, what, when));
    }

    internal static string FormatSize(long bytes) => bytes >= 1 << 20
        ? (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
        : Math.Max(1, (long)Math.Ceiling(bytes / 1024.0)).ToString(CultureInfo.CurrentCulture) + " kB";
}
