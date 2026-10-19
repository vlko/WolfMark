using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Calendar;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Tags;
using WolfMark.Index.Indexing;
using WolfMark.Index.Queries;
using WolfMark.Sync.ClaudeCode;

namespace WolfMark.App.ViewModels;

/// <summary>Orchestrates the main window: selection, refresh on index changes, overlays.</summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly ILogger _logger;
    private readonly Debouncer _historyRefresh = new(TimeSpan.FromMilliseconds(300));
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
        Today = DateOnly.FromDateTime(DateTime.Now);
        SelectedDay = Today;
        IsCalendarExpanded = host.Settings.Current.CalendarExpanded;

        Editor.PathChanged += (_, _) => History.MarkCurrent(Editor.IsPersisted ? Editor.Path?.RelativePath : null);
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

    public AppHost Host => _host;

    /// <summary>Opens the last note immediately (no index needed), then loads the rest.</summary>
    public async Task InitializeAsync()
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
            SelectedDay = Editor.Day;
        }
        else
        {
            await Editor.NewAsync(null);
        }

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
        if (e.IsFullScan || e.Days.Any(d => d >= Week.Monday && d <= Week.Monday.AddDays(6)))
        {
            await Week.LoadAsync(Week.Monday, SelectedDay);
        }

        if (IsCalendarExpanded)
        {
            if (e.IsFullScan)
            {
                await Calendar.LoadAsync();
            }
            else
            {
                await Calendar.RefreshDaysAsync(e.Days);
            }
        }

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
