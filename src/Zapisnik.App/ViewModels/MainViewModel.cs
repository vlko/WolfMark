using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Zapisnik.App.Infrastructure;
using Zapisnik.Core.Calendar;
using Zapisnik.Core.Events;
using Zapisnik.Core.Notes;
using Zapisnik.Index.Indexing;
using Zapisnik.Index.Queries;
using Zapisnik.Sync.ClaudeCode;

namespace Zapisnik.App.ViewModels;

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

        Editor.PathChanged += (_, _) => History.MarkCurrent(Editor.IsPersisted ? Editor.Path?.RelativePath : null);
        Search.ResultInvoked += async (_, note) =>
        {
            IsSearchOpen = false;
            await OpenNoteAsync(note.RelativePath);
        };

        host.Indexer.Changed += (_, e) => UiThread.Post(() => _ = OnIndexChangedAsync(e));
        host.IndexProgressChanged += (_, p) => UiThread.Post(() => OnIndexProgress(p));
        host.ClaudeSynced += (_, r) => UiThread.Post(() => OnClaudeSynced(r));
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

    /// <summary>Tag pills of the selected day ("● praca 3").</summary>
    public ObservableCollection<DayTagViewModel> DayTags { get; } = [];

    [ObservableProperty]
    public partial DateOnly Today { get; private set; }

    [ObservableProperty]
    public partial DateOnly SelectedDay { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int ConflictCount { get; private set; }

    [ObservableProperty]
    public partial CleanupStatus? CleanupWarning { get; private set; }

    [ObservableProperty]
    public partial bool IsCalendarOpen { get; set; }

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
        await LoadDayTagsAsync();
        await RefreshConflictsAsync();
        await CheckClaudeCleanupAsync();
    }

    public async Task SelectDayAsync(DateOnly day, bool openLatest = true)
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

        await History.SetDayFilterAsync(day);
        await LoadDayTagsAsync();

        if (!openLatest)
        {
            return;
        }

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
            await LoadDayTagsAsync();
        }
    }

    public async Task NewNoteAsync()
    {
        await Editor.NewAsync(History.TagFilter);
        if (SelectedDay != Today)
        {
            SelectedDay = Today;
            await Week.LoadAsync(Today, Today);
            await LoadDayTagsAsync();
        }
    }

    /// <summary>Raised after the calendar finished loading for <see cref="OpenCalendarAsync"/>.</summary>
    public event EventHandler? CalendarLoaded;

    public async Task OpenCalendarAsync()
    {
        IsSearchOpen = false;
        IsCalendarOpen = true;
        await Calendar.GoToAsync(SelectedDay);
        CalendarLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void OpenSearch()
    {
        IsCalendarOpen = false;
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
            await SelectDayAsync(today, openLatest: false);
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

        if (e.IsFullScan || e.Days.Contains(SelectedDay))
        {
            await LoadDayTagsAsync();
        }

        if (IsCalendarOpen)
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
            await RefreshConflictsAsync();
        });
    }

    private void OnIndexProgress(IndexProgress p)
    {
        StatusText = p.Phase == "done" || p.Total == 0 ? string.Empty : Strings.Format(R.Indexing_Progress, p.Processed, p.Total);
    }

    private void OnClaudeSynced(SyncResult r)
    {
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

    private async Task LoadDayTagsAsync()
    {
        await _host.DatabaseReady;
        var days = await _host.Calendar.GetDaysAsync(SelectedDay, SelectedDay, _host.ShutdownToken);
        DayTags.Clear();
        if (days.GetValueOrDefault(SelectedDay) is { } day)
        {
            foreach (var t in day.Tags)
            {
                DayTags.Add(new DayTagViewModel(t.Tag.Length == 0 ? null : t.Tag, t.Count));
            }
        }
    }

    private async Task RefreshConflictsAsync()
    {
        await _host.DatabaseReady;
        ConflictCount = (await _host.History.GetConflictsAsync(_host.ShutdownToken)).Count;
    }

    private async Task CheckClaudeCleanupAsync()
    {
        var claudeDirectory = _host.ClaudeDirectory;
        if (!_host.Settings.Current.ClaudeCode.Enabled || !await Task.Run(() => Directory.Exists(claudeDirectory)))
        {
            return;
        }

        var status = await Task.Run(() => CleanupPeriodCheck.CheckAsync(Path.Combine(claudeDirectory, "settings.json"), CancellationToken.None));
        CleanupWarning = status.IsRisky ? status : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Claude Code sync: {Created} created, {Updated} updated")]
    private partial void LogSynced(int created, int updated);
}

internal sealed partial class DayTagViewModel(string? tag, int count) : ObservableObject
{
    public string? Tag { get; } = tag;

    public int Count { get; } = count;
}
