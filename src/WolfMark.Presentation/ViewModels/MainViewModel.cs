using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Calendar;
using WolfMark.Core.Events;
using WolfMark.Core.Links;
using WolfMark.Core.Markdown;
using WolfMark.Core.Notes;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Index.Indexing;
using WolfMark.Index.Queries;
using WolfMark.Storage.Tags;
using WolfMark.Sync.ClaudeCode;

namespace WolfMark.App.ViewModels;

/// <summary>Orchestrates the main window: selection, refresh on index changes, overlays.</summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly ILogger _logger;
    private readonly Debouncer _historyRefresh = new(TimeSpan.FromMilliseconds(300));
    private readonly Debouncer _externalSave = new(TimeSpan.FromSeconds(1));
    private readonly HashSet<DateOnly> _pendingDays = [];
    private bool _initialized;

    public MainViewModel(AppHost host)
    {
        _host = host;
        _logger = host.Loggers.CreateLogger<MainViewModel>();
        Editor = new NoteEditorViewModel(host);
        Week = new WeekStripViewModel(host);
        History = new HistoryViewModel(host);
        Calendar = new CalendarViewModel(host);
        Search = new SearchViewModel(host);
        Find = new FindViewModel();
        Today = DateOnly.FromDateTime(DateTime.Now);
        SelectedDay = Today;
        IsCalendarExpanded = host.Settings.Current.CalendarExpanded;

        Editor.PathChanged += (_, _) => History.MarkCurrent(Editor.IsPersisted ? Editor.Path?.RelativePath : null);
        Editor.PathChanged += (_, _) => _ = LoadBacklinksAsync();
        // Another note in the editor (opened, new, reloaded from disk): a find bar left open would show the query and
        // the count of the note before.
        Editor.LoadRequested += (_, _) => Find.Close();
        Search.ResultInvoked += async (_, note) =>
        {
            IsSearchOpen = false;
            await OpenNoteAsync(note.RelativePath);
        };

        host.Indexer.Changed += (_, e) => UiThread.Post(() => _ = OnIndexChangedAsync(e));
        host.IndexProgressChanged += (_, p) => UiThread.Post(() => OnIndexProgress(p));
        host.ClaudeSynced += (_, r) => UiThread.Post(() => OnClaudeSynced(r));
        host.ClaudeSyncFailed += (_, _) => UiThread.Post(() =>
        {
            ClaudeStatusIsError = true;
            ClaudeStatus = Strings.Format(R.Status_ClaudeFailed, DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture));
        });
        host.StartupFailed += (_, ex) => UiThread.Post(() => ErrorNotice = Strings.Format(R.Error_Startup, ex.Message));
        if (host.StartupError is { } startupError)
        {
            ErrorNotice = Strings.Format(R.Error_Startup, startupError.Message);
        }
        else if (host.Settings.InvalidFileCopy is { } settingsCopy)
        {
            ErrorNotice = Strings.Format(R.Error_SettingsInvalid, settingsCopy);
        }

        var events = host.Hub.Subscribe();
        _ = host.Background.Run("ui-events", async ct =>
        {
            await foreach (var e in events.ReadAllAsync(ct))
            {
                UiThread.Post(() => _ = OnNoteEventAsync(e));
            }
        }, host.ShutdownToken);
    }

    public NoteEditorViewModel Editor { get; }

    /// <summary>Find and replace in the open note (Ctrl+F, Ctrl+H).</summary>
    public FindViewModel Find { get; }

    public WeekStripViewModel Week { get; }

    public HistoryViewModel History { get; }

    public CalendarViewModel Calendar { get; }

    public SearchViewModel Search { get; }

    /// <summary>Tags of the right-hand rail, most used first.</summary>
    public ObservableCollection<RailTagViewModel> RailTags { get; } = [];

    [ObservableProperty]
    public partial DateOnly Today { get; private set; }

    [ObservableProperty]
    public partial DateOnly SelectedDay { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ConflictCount { get; private set; }

    /// <summary>Status bar: indexing in progress (then <see cref="StatusText"/> shows the progress).</summary>
    [ObservableProperty]
    public partial bool IsIndexing { get; private set; }

    [ObservableProperty]
    public partial int NoteCount { get; private set; }

    /// <summary>Status bar: Claude Code import state ("Claude Code · 14:20 · 2 nových, 1 aktualizovaných").</summary>
    [ObservableProperty]
    public partial string ClaudeStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ClaudeStatusIsError { get; private set; }

    /// <summary>Error notice at the bottom (message), null = hidden. Details are in the log.</summary>
    [ObservableProperty]
    public partial string? ErrorNotice { get; set; }

    /// <summary>Sample notice from Settings → Diagnostics, to try the notice area.</summary>
    [ObservableProperty]
    public partial bool IsTestNoticeOpen { get; set; }

    [ObservableProperty]
    public partial CleanupStatus? CleanupWarning { get; private set; }

    /// <summary>Calendar panel shows the whole month instead of the week band.</summary>
    [ObservableProperty]
    public partial bool IsCalendarExpanded { get; private set; }

    /// <summary>Tag chosen in the rail: filters history and dims other days; new notes get it.</summary>
    [ObservableProperty]
    public partial string? ActiveTag { get; private set; }

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    /// <summary>
    /// The note fills the window: day heading, formatting and the editor only – no tag rail, calendar, history or
    /// time gutter. Not remembered: a new start shows the whole app.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFullView { get; set; }

    public AppHost Host => _host;

    /// <summary>Opens the last note immediately (no index needed), then loads the rest.</summary>
    /// <param name="noteReady">
    /// Called once the start note is in the editor – before the week, history, calendar and counts load (M4-09: the
    /// user can type while they fill in).
    /// </param>
    public async Task InitializeAsync(Action? noteReady = null)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var last = _host.Settings.Current.LastNotePath;
        if (last is not null && NotePath.TryParse(last, out var lastPath)
            && await Task.Run(() => File.Exists(_host.Engine.GetFullPath(lastPath))))
        {
            await Editor.OpenAsync(lastPath, caretToEnd: true);
            WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("startup: note opened");
            SelectedDay = Editor.Day;
        }
        else
        {
            await Editor.NewAsync(null);
        }

        noteReady?.Invoke();
        await Week.LoadAsync(SelectedDay, SelectedDay);
        await History.ReloadAsync();
        History.MarkCurrent(Editor.IsPersisted ? Editor.Path?.RelativePath : null);
        await SyncCalendarMonthAsync(force: true);
        await LoadRailTagsAsync();
        await RefreshConflictsAsync();
        await RefreshNoteCountAsync();
        await CheckClaudeCleanupAsync();
    }

    /// <summary>"Today": back to the start-up mode – today selected, history of all notes (no day filter).</summary>
    public Task GoToTodayAsync() => SelectDayAsync(Today, openLatest: true, filterHistory: false);

    /// <param name="filterHistory">True when the user picked a specific day: history shows only that day.</param>
    public async Task SelectDayAsync(DateOnly day, bool openLatest = true, bool filterHistory = true)
    {
        SelectedDay = day;
        if (day < Week.Monday || day > Week.Monday.AddDays(6))
        {
            await Week.LoadAsync(day, day);
        }
        else
        {
            Week.Select(day);
        }

        // The note first – that is what the user looks at; the history and month grid follow.
        if (openLatest)
        {
            await _host.DatabaseReady;
            var latest = await _host.History.GetLatestOnDayAsync(day, _host.ShutdownToken);
            if (latest is not null && NotePath.TryParse(latest.RelativePath, out var path))
            {
                await Editor.OpenAsync(path);
            }
            else if (day == Today)
            {
                await Editor.NewAsync(null);
            }
        }

        await History.SetDayFilterAsync(filterHistory ? day : null);
        await SyncCalendarMonthAsync();
    }

    public Task MoveWeekAsync(int weeks) => Week.LoadAsync(Week.Monday.AddDays(7 * weeks), SelectedDay);

    /// <summary>History context menu (M5-04): <c>[[2026/11/16/praca_1403]]</c> – the note's path without the extension.</summary>
    public static string NoteLink(string relativePath) =>
        "[[" + (relativePath.EndsWith(NoteName.Extension, StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^NoteName.Extension.Length]
            : relativePath) + "]]";

    /// <summary>History context menu: the open note goes through the editor (it keeps saving to the renamed file).</summary>
    public async Task ChangeNoteTagAsync(string relativePath, string? tag)
    {
        if (IsOpenInEditor(relativePath))
        {
            await Editor.ChangeTagAsync(tag);
        }
        else if (NotePath.TryParse(relativePath, out var path))
        {
            await Task.Run(() => _host.Engine.ChangeTagAsync(path, tag, CancellationToken.None));
        }
    }

    /// <summary>History context menu: to the Recycle Bin / trash, never permanently (CLAUDE.md rule 5).</summary>
    public async Task DeleteNoteAsync(string relativePath)
    {
        if (IsOpenInEditor(relativePath))
        {
            await Editor.DeleteAsync();
        }
        else if (NotePath.TryParse(relativePath, out var path))
        {
            await Task.Run(() => _host.Engine.DeleteAsync(path, CancellationToken.None));
        }
    }

    /// <summary>The conflict count in the status bar: opens the first OneDrive conflict copy.</summary>
    public async Task OpenFirstConflictAsync()
    {
        var list = await _host.History.GetConflictsAsync(_host.ShutdownToken);
        if (list.Count > 0)
        {
            await OpenNoteAsync(list[0].RelativePath);
        }
    }

    /// <summary>The outcome of moving the data (notes folder, portable mode) for the Settings window.</summary>
    /// <param name="RestartNow">Done: nothing may write into the old place any more, the app restarts at once.</param>
    /// <param name="Message">What to show instead: a failure, or what could not be cleaned up afterwards.</param>
    /// <param name="Failed">Nothing was switched (watcher and sync are stopped all the same – a restart brings them back).</param>
    internal sealed record RelocationOutcome(bool RestartNow, string? Message, bool Failed);

    /// <summary>Settings → notes folder → "Move notes": copy, verify, old folder to the recycle bin, new root saved.</summary>
    public async Task<RelocationOutcome> MoveNotesAsync(string newRoot)
    {
        await Editor.FlushAsync();
        try
        {
            await _host.MoveNotesAsync(newRoot, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRelocationFailed(ex, newRoot);
            return new RelocationOutcome(false, Strings.Format(R.Settings_MoveFailed, ex.Message), Failed: true);
        }

        await SwitchNotesRootAsync(newRoot);
        return new RelocationOutcome(true, null, Failed: false);
    }

    /// <summary>Settings → notes folder → "Just switch": the other folder from the next start on.</summary>
    public async Task SwitchNotesRootAsync(string newRoot)
    {
        _host.Settings.Current.RootDirectory = _host.Settings.ToStoredRoot(newRoot);
        await _host.SaveSettingsAsync();
    }

    /// <summary>
    /// Settings → portable mode (ADR 0009): into the folder next to the app, or back to the user profile when the
    /// app runs portable.
    /// </summary>
    /// <param name="removeSystemIntegration">
    /// After switching portable mode on: takes back what the app registered on this computer (start at sign-in, file
    /// association, links) – portable mode leaves nothing there. Best effort, off the UI thread.
    /// </param>
    public async Task<RelocationOutcome> SwitchPortableAsync(bool moveNotes, IProgress<int>? progress, Action? removeSystemIntegration)
    {
        var enable = _host.PortableDataDirectory is null;
        await Editor.FlushAsync();
        try
        {
            var result = enable
                ? await _host.EnablePortableAsync(moveNotes, progress)
                : await _host.DisablePortableAsync(progress);
            if (enable && removeSystemIntegration is not null)
            {
                await Task.Run(removeSystemIntegration);
            }

            return result.CleanupError is { } cleanup
                ? new RelocationOutcome(false, Strings.Format(R.Settings_PortableCleanupFailed, cleanup.Message), Failed: false)
                : new RelocationOutcome(true, null, Failed: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRelocationFailed(ex, enable ? "portable" : "standard");
            return new RelocationOutcome(false, Strings.Format(R.Settings_PortableFailed, ex.Message), Failed: true);
        }
    }

    /// <summary>
    /// Ctrl + / Ctrl − / Ctrl 0 (null = back to the default) in any editor: the font size setting, saved once the
    /// zooming stops.
    /// </summary>
    /// <returns>True when the size changed – the view applies it to its editors.</returns>
    public bool ZoomEditor(int? steps)
    {
        var s = _host.Settings.Current;
        var size = steps is { } n ? LocalSettings.ZoomFontSize(s.EditorFontSize, n) : LocalSettings.EditorFontSizeDefault;
        if (size == s.EditorFontSize)
        {
            return false;
        }

        s.EditorFontSize = size;
        _externalSave.Schedule(_ => _host.SaveSettingsAsync());
        return true;
    }

    private bool IsOpenInEditor(string relativePath) =>
        string.Equals(Editor.Path?.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase);

    public async Task OpenNoteAsync(string relativePath)
    {
        if (!NotePath.TryParse(relativePath, out var path))
        {
            return;
        }

        await Editor.OpenAsync(path);
        var day = Editor.Day;
        if (day != SelectedDay)
        {
            SelectedDay = day;
            await Week.LoadAsync(day, day);
            await SyncCalendarMonthAsync();
        }
    }

    /// <summary>
    /// A Markdown file from outside (file association, command line, a second start): a note of the notes tree
    /// opens here, any other file goes to <see cref="ExternalFileRequested"/> – the view opens it in its own window.
    /// </summary>
    public async Task OpenFileAsync(string fullPath)
    {
        var root = _host.Engine.Root;
        if (NotesTree.TryGetNotePath(root, fullPath, out var path))
        {
            IsFullView = false;
            await OpenNoteAsync(path.RelativePath);
            return;
        }

        if (!await Task.Run(() => File.Exists(fullPath)))
        {
            ErrorNotice = Strings.Format(R.ExtFile_Missing, fullPath);
            return;
        }

        ExternalFileRequested?.Invoke(this, fullPath);
    }

    /// <summary>A file outside the notes tree is to be shown in an external window.</summary>
    public event EventHandler<string>? ExternalFileRequested;

    /// <summary>An external file was opened or saved: it goes to the top of the history.</summary>
    public void TouchExternalFile(string fullPath)
    {
        ExternalFileHistory.Touch(_host.Settings.Current.ExternalFiles, fullPath, DateTime.UtcNow);
        _externalSave.Schedule(_ => _host.SaveSettingsAsync());
        _ = History.ReloadAsync();
    }

    public void RemoveExternalFile(string fullPath)
    {
        if (ExternalFileHistory.Remove(_host.Settings.Current.ExternalFiles, fullPath))
        {
            _externalSave.Schedule(_ => _host.SaveSettingsAsync());
            _ = History.ReloadAsync();
        }
    }

    /// <summary>
    /// A <c>wolfmark://</c> link to a day, a note or today (M8-03) – the whole app is shown (no full view, no
    /// search). The quick note and the search with its text are the view's (<see cref="AppLinkKind.New"/>,
    /// <see cref="AppLinkKind.Search"/>). A note that does not exist is reported, nothing is created.
    /// </summary>
    /// <summary>
    /// Follows a wiki link from the open note (M9-04) – <paramref name="inner"/> is the text between the brackets:
    /// a note by its path or by its file name alone, or a day. A link that leads nowhere says so.
    /// </summary>
    public async Task OpenWikiLinkAsync(string inner)
    {
        if (WikiLinks.Parse(inner) is not { } target)
        {
            ErrorNotice = Strings.Format(R.Link_NoteMissing, inner);
            return;
        }

        switch (target.Kind)
        {
            case WikiTargetKind.Day:
                await SelectDayAsync(target.Day);
                break;
            case WikiTargetKind.Note:
                var full = _host.Engine.GetFullPath(target.Value);
                if (await Task.Run(() => File.Exists(full)))
                {
                    await OpenNoteAsync(target.Value);
                }
                else
                {
                    ErrorNotice = Strings.Format(R.Link_NoteMissing, target.Value);
                }

                break;
            case WikiTargetKind.Name:
                var found = _host.DatabaseReady.IsCompletedSuccessfully
                    ? await _host.History.FindByFileNameAsync(target.Value, _host.ShutdownToken)
                    : null;
                if (found is not null)
                {
                    await OpenNoteAsync(found);
                }
                else
                {
                    ErrorNotice = Strings.Format(R.Link_NoteMissing, target.Value);
                }

                break;
        }
    }

    /// <summary>Notes that link to the open one, the newest first (M9-04); empty for a note not saved yet.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NoteSummary> Backlinks { get; private set; } = [];

    private int _backlinksGeneration;

    /// <summary>After the open note changed and after every index change (a link may have been written anywhere).</summary>
    private async Task LoadBacklinksAsync()
    {
        var generation = ++_backlinksGeneration;
        IReadOnlyList<NoteSummary> links = [];
        if (Editor is { IsPersisted: true, Path: { } path } && _host.DatabaseReady.IsCompletedSuccessfully)
        {
            try
            {
                links = await _host.History.GetBacklinksAsync(path, _host.ShutdownToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
            {
                return; // shutting down, or the index is being rebuilt – the next index change loads them
            }
        }

        if (generation == _backlinksGeneration && (links.Count > 0 || Backlinks.Count > 0))
        {
            Backlinks = links;
        }
    }

    public async Task OpenLinkAsync(AppLink link)
    {
        IsFullView = false;
        IsSearchOpen = false;
        switch (link.Kind)
        {
            case AppLinkKind.Today:
                await GoToTodayAsync();
                break;
            case AppLinkKind.Day:
                await SelectDayAsync(link.Day!.Value);
                break;
            case AppLinkKind.Note:
                var path = link.Note!;
                if (await Task.Run(() => File.Exists(_host.Engine.GetFullPath(path))))
                {
                    await OpenNoteAsync(path.RelativePath);
                }
                else
                {
                    ErrorNotice = Strings.Format(R.Link_NoteMissing, path.RelativePath);
                }

                break;
        }
    }

    public async Task NewNoteAsync()
    {
        await Editor.NewAsync(ActiveTag);
        if (SelectedDay != Today)
        {
            SelectedDay = Today;
            await Week.LoadAsync(Today, Today);
            await SyncCalendarMonthAsync();
        }
    }

    public async Task ToggleCalendarAsync()
    {
        IsCalendarExpanded = !IsCalendarExpanded;
        _host.Settings.Current.CalendarExpanded = IsCalendarExpanded;
        _ = _host.SaveSettingsAsync();
        await SyncCalendarMonthAsync(force: true);
    }

    /// <summary>Previous/next: weeks in the band, months in the expanded grid.</summary>
    public Task MoveCalendarAsync(int delta) => IsCalendarExpanded ? Calendar.MoveAsync(delta) : MoveWeekAsync(delta);

    public async Task SetActiveTagAsync(string? tag)
    {
        ActiveTag = tag;
        Calendar.SetFilter(tag);
        foreach (var t in RailTags)
        {
            t.IsActive = tag is not null && TagName.Comparer.Equals(t.Tag, tag);
        }

        await History.SetTagFilterAsync(tag);
    }

    /// <summary>
    /// Settings → Tags (M8-05): renames <paramref name="oldTag"/> in the names of all its notes; to an existing tag it
    /// merges them; null removes the tag from the notes (they stay, untagged). On a failure the files done are
    /// renamed back (see the result).
    /// </summary>
    public async Task<TagRenamer.Result> RenameTagAsync(string oldTag, string? newTag, IProgress<int>? progress)
    {
        await Editor.FlushAsync(); // no autosave into the old file name while it is renamed
        var engine = _host.Engine;
        var notes = await Task.Run(() => TagRenamer.FindAsync(engine, oldTag, _host.ShutdownToken));
        var moved = new List<(string Old, string New)>();
        var result = await Task.Run(() => TagRenamer.RenameAsync(
            notes,
            newTag,
            async (path, tag) =>
            {
                var renamed = await engine.ChangeTagAsync(path, tag, CancellationToken.None);
                moved.Add((path.RelativePath, renamed.RelativePath));
                return renamed;
            },
            progress,
            (renamed, original) => engine.RenameAsync(renamed, original, CancellationToken.None)));
        if (result.Error is not null)
        {
            LogTagRenameFailed(result.Error, oldTag, newTag ?? "-", result.RollbackFailures.Count);
            return result; // the rename events (both ways) bring the index up to date
        }

        // Index now, not when the rename events get processed: the page lists the tags right after this.
        var upserts = moved.Select(m => m.New).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletes = moved.Select(m => m.Old).Where(o => !upserts.Contains(o)).ToList(); // a case-only rename is the same file
        await Task.Run(() => _host.Indexer.ApplyAsync(upserts, deletes, _host.ShutdownToken));
        await Task.Run(() => newTag is null
            ? _host.Tags.RemoveAsync(oldTag, CancellationToken.None)
            : _host.Tags.RenameAsync(oldTag, newTag, CancellationToken.None));
        Editor.OnTagRenamed(oldTag, newTag);
        if (ActiveTag is { } active && TagName.Comparer.Equals(active, oldTag))
        {
            await SetActiveTagAsync(newTag);
        }

        LogTagRenamed(oldTag, newTag ?? "-", result.Renamed);
        return result;
    }

    public void OpenSearch()
    {
        Search.Reset();
        IsSearchOpen = true;
    }

    public async Task FixClaudeCleanupAsync()
    {
        var path = Path.Combine(_host.ClaudeDirectory, "settings.json");
        await Task.Run(() => CleanupPeriodCheck.SetAsync(path, CleanupStatus.Recommended, CancellationToken.None));
        CleanupWarning = null;
    }

    /// <summary>Called by a minute timer: the date changes at midnight.</summary>
    public async Task TickAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == Today)
        {
            return;
        }

        var followToday = SelectedDay == Today;
        Today = today;
        if (followToday)
        {
            // Keep the history mode the user was in (filtered by the day, or all notes).
            await SelectDayAsync(today, openLatest: false, filterHistory: History.DayFilter is not null);
        }
        else
        {
            await Week.LoadAsync(Week.Monday, SelectedDay);
        }
    }

    private async Task OnIndexChangedAsync(IndexChangedEventArgs e)
    {
        WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark($"index changed ({e.Paths.Count} notes)");
        if (e.IsFullScan || e.Days.Any(d => d >= Week.Monday && d <= Week.Monday.AddDays(6)))
        {
            await Week.LoadAsync(Week.Monday, SelectedDay);
        }

        if (IsCalendarExpanded)
        {
            WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("calendar");
            if (e.IsFullScan)
            {
                await Calendar.LoadAsync();
            }
            else
            {
                await Calendar.RefreshDaysAsync(e.Days);
            }
        }

        _ = LoadBacklinksAsync();
        _pendingDays.UnionWith(e.Days);
        _historyRefresh.Schedule(async _ =>
        {
            _pendingDays.Clear();
            await History.ReloadAsync();
            await LoadRailTagsAsync();
            await RefreshConflictsAsync();
            await RefreshNoteCountAsync();
        });
    }

    private void OnIndexProgress(IndexProgress p)
    {
        IsIndexing = p.Phase != "done" && p.Total > 0;
        StatusText = IsIndexing ? Strings.Format(R.Indexing_Progress, p.Processed, p.Total) : string.Empty;
        if (!IsIndexing)
        {
            _ = RefreshNoteCountAsync();
        }
    }

    private void OnClaudeSynced(SyncResult r)
    {
        var c = System.Globalization.CultureInfo.CurrentCulture;
        var text = Strings.Format(R.Status_ClaudeSynced, DateTime.Now.ToString("t", c));
        if (r.NotesCreated + r.NotesUpdated > 0)
        {
            text += " · " + Strings.Format(R.Status_ClaudeChanges, r.NotesCreated, r.NotesUpdated);
        }

        ClaudeStatusIsError = false;
        ClaudeStatus = text;
        if (r.NotesCreated + r.NotesUpdated > 0)
        {
            LogSynced(r.NotesCreated, r.NotesUpdated);
        }
    }

    private async Task OnNoteEventAsync(NoteEvent e)
    {
        if (e is TagsChanged)
        {
            await Task.Run(() => _host.Tags.LoadAsync(_host.ShutdownToken));
            return;
        }

        await Editor.OnNoteEventAsync(e);
    }

    /// <summary>Keeps the expanded month on the month of the selected day.</summary>
    private async Task SyncCalendarMonthAsync(bool force = false)
    {
        if (IsCalendarExpanded && (force || Calendar.Year != SelectedDay.Year || Calendar.Month != SelectedDay.Month))
        {
            await Calendar.GoToAsync(SelectedDay);
        }
    }

    private async Task LoadRailTagsAsync()
    {
        await _host.DatabaseReady;
        var usage = await _host.Calendar.GetTagUsageAsync(null, null, _host.ShutdownToken);
        var known = RailTags.Select(t => t.Tag).ToList();
        var fresh = usage.Select(u => u.Tag).ToList();
        if (known.SequenceEqual(fresh, TagName.Comparer))
        {
            return; // unchanged – keep the views (and their fold state)
        }

        RailTags.Clear();
        foreach (var u in usage)
        {
            RailTags.Add(new RailTagViewModel(u.Tag, u.Count) { IsActive = ActiveTag is not null && TagName.Comparer.Equals(ActiveTag, u.Tag) });
        }
    }

    private async Task RefreshConflictsAsync()
    {
        await _host.DatabaseReady;
        ConflictCount = (await _host.History.GetConflictsAsync(_host.ShutdownToken)).Count;
    }

    private async Task RefreshNoteCountAsync()
    {
        await _host.DatabaseReady;
        NoteCount = await _host.History.CountAsync(_host.ShutdownToken);
    }

    private async Task CheckClaudeCleanupAsync()
    {
        var enabled = _host.Settings.Current.ClaudeCode.Enabled;
        var claudeDirectory = _host.ClaudeDirectory;
        var exists = enabled && await Task.Run(() => Directory.Exists(claudeDirectory));
        if (string.IsNullOrEmpty(ClaudeStatus))
        {
            ClaudeStatus = Strings.Get(!enabled ? R.Status_ClaudeOff : !exists ? R.Status_ClaudeMissing : R.Status_ClaudeWaiting);
        }

        if (!exists)
        {
            return;
        }

        var status = await Task.Run(() => CleanupPeriodCheck.CheckAsync(Path.Combine(claudeDirectory, "settings.json"), CancellationToken.None));
        CleanupWarning = status.IsRisky ? status : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tag {Old} renamed to {New} in {Count} notes")]
    private partial void LogTagRenamed(string old, string @new, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Moving the data to {Target} failed")]
    private partial void LogRelocationFailed(Exception ex, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Renaming tag {Old} to {New} failed; {NotRolledBack} notes could not be renamed back")]
    private partial void LogTagRenameFailed(Exception ex, string old, string @new, int notRolledBack);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claude Code sync: {Created} created, {Updated} updated")]
    private partial void LogSynced(int created, int updated);
}

internal sealed partial class RailTagViewModel(string tag, int count) : ObservableObject
{
    public string Tag { get; } = tag;

    public int Count { get; } = count;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
