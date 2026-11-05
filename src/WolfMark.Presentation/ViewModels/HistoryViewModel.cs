using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Notes;
using WolfMark.Index.Queries;
using WolfMark.Storage;

namespace WolfMark.App.ViewModels;

/// <summary>
/// History list (M5-01/M5-02): ordered by the last edit, keyset paging, tag/day filters, headers per
/// day of the last edit. External files (opened from outside the notes tree) are merged in by the time they were
/// last opened or saved; they have no tag and no tasks, so the tag and open-tasks filters leave them out.
/// </summary>
internal sealed partial class HistoryViewModel(AppHost host) : ObservableObject
{
    private const int PageSize = 60;

    private HistoryCursor? _next;
    private DateOnly? _lastDay;
    private int _generation;
    private string? _currentPath;
    private bool _loaded;
    private List<HistoryExternalRowViewModel> _externals = [];
    private int _externalIndex;
    private readonly Dictionary<string, ExternalPreview> _previews = new(StringComparer.Ordinal);

    /// <summary><see cref="HistoryHeaderViewModel"/>, <see cref="HistoryRowViewModel"/> and <see cref="HistoryExternalRowViewModel"/> items.</summary>
    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    public partial string? TagFilter { get; private set; }

    [ObservableProperty]
    public partial DateOnly? DayFilter { get; private set; }

    [ObservableProperty]
    public partial bool OpenTasksOnly { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool HasFilter => TagFilter is not null || DayFilter is not null || OpenTasksOnly;

    public string FilterLabel
    {
        get
        {
            var parts = new List<string>();
            if (TagFilter is not null)
            {
                parts.Add("#" + TagFilter);
            }

            if (DayFilter is { } d)
            {
                parts.Add(d.ToString("d", CultureInfo.CurrentCulture));
            }

            if (OpenTasksOnly)
            {
                parts.Add(Strings.Get(R.History_OpenTasks));
            }

            return string.Join(" · ", parts);
        }
    }

    public Task SetTagFilterAsync(string? tag)
    {
        TagFilter = tag;
        return ReloadAsync();
    }

    public Task SetDayFilterAsync(DateOnly? day)
    {
        // Same filter: the list is current – index changes reload it on their own (MainViewModel).
        if (day == DayFilter && _loaded)
        {
            return Task.CompletedTask;
        }

        DayFilter = day;
        return ReloadAsync();
    }

    public Task ToggleOpenTasksAsync()
    {
        OpenTasksOnly = !OpenTasksOnly;
        return ReloadAsync();
    }

    public Task ClearFiltersAsync()
    {
        TagFilter = null;
        DayFilter = null;
        OpenTasksOnly = false;
        return ReloadAsync();
    }

    public void MarkCurrent(string? relativePath)
    {
        _currentPath = relativePath;
        foreach (var item in Items)
        {
            if (item is HistoryRowViewModel row)
            {
                row.IsCurrent = string.Equals(row.Note.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public async Task ReloadAsync()
    {
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterLabel));
        var generation = ++_generation;
        var externals = await LoadExternalsAsync();
        await host.DatabaseReady;
        IsLoading = true;
        try
        {
            IReadOnlyList<NoteSummary> items;
            HistoryCursor? next = null;
            if (OpenTasksOnly)
            {
                items = await host.History.GetWithOpenTasksAsync(500, host.ShutdownToken);
            }
            else
            {
                var page = await host.History.GetPageAsync(new HistoryQuery(TagFilter, DayFilter, null, PageSize), host.ShutdownToken);
                items = page.Items;
                next = page.Next;
            }

            if (generation != _generation)
            {
                return;
            }

            _lastDay = null;
            _next = next;
            _externals = externals;
            _externalIndex = 0;
            Replace([.. Entries(items, lastPage: next is null)]);
            IsEmpty = Items.Count == 0;
            _loaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_next is null || IsLoading)
        {
            return;
        }

        var generation = _generation;
        IsLoading = true;
        try
        {
            var page = await host.History.GetPageAsync(new HistoryQuery(TagFilter, DayFilter, _next, PageSize), host.ShutdownToken);
            if (generation != _generation)
            {
                return;
            }

            _next = page.Next;
            Append(page.Items, lastPage: page.Next is null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Append(IReadOnlyList<NoteSummary> notes, bool lastPage)
    {
        foreach (var entry in Entries(notes, lastPage))
        {
            Items.Add(entry);
        }
    }

    /// <summary>
    /// Headers and rows for <paramref name="notes"/>, continuing after <see cref="_lastDay"/>; the external files
    /// newer than each note come before it, the rest after the last page.
    /// </summary>
    private IEnumerable<object> Entries(IReadOnlyList<NoteSummary> notes, bool lastPage)
    {
        foreach (var note in notes)
        {
            while (_externalIndex < _externals.Count && _externals[_externalIndex].TouchedAt > note.ModifiedAt)
            {
                var external = _externals[_externalIndex++];
                foreach (var entry in Entry(external, external.TouchedAt))
                {
                    yield return entry;
                }
            }

            var row = new HistoryRowViewModel(note)
            {
                IsCurrent = string.Equals(note.RelativePath, _currentPath, StringComparison.OrdinalIgnoreCase),
            };
            foreach (var entry in Entry(row, note.ModifiedAt))
            {
                yield return entry;
            }
        }

        while (lastPage && _externalIndex < _externals.Count)
        {
            var external = _externals[_externalIndex++];
            foreach (var entry in Entry(external, external.TouchedAt))
            {
                yield return entry;
            }
        }
    }

    /// <summary>The row, after a header when it starts a new day (the list is grouped by the day of the last edit).</summary>
    private IEnumerable<object> Entry(object row, DateTime editedAt)
    {
        var editedDay = DateOnly.FromDateTime(editedAt);
        if (DayFilter is null && editedDay != _lastDay)
        {
            yield return new HistoryHeaderViewModel(HeaderLabel(editedDay));
            _lastDay = editedDay;
        }

        yield return row;
    }

    /// <summary>
    /// The external files the filters let through, newest first. Their title and preview come from the file
    /// (read off the UI thread, cached until it changes); a file that is gone stays in the list, dimmed.
    /// </summary>
    private async Task<List<HistoryExternalRowViewModel>> LoadExternalsAsync()
    {
        if (TagFilter is not null || OpenTasksOnly)
        {
            return [];
        }

        var day = DayFilter;
        var entries = host.Settings.Current.ExternalFiles
            .Select(e => (e.Path, TouchedAt: e.TouchedUtc.ToLocalTime()))
            .Where(e => day is null || DateOnly.FromDateTime(e.TouchedAt) == day)
            .OrderByDescending(e => e.TouchedAt)
            .ToList();
        if (entries.Count == 0)
        {
            return [];
        }

        var cache = _previews;
        var previews = await Task.Run(() => entries.Select(e => ReadPreview(e.Path, cache)).ToList());
        return [.. entries.Select((e, i) => new HistoryExternalRowViewModel(e.Path, e.TouchedAt, previews[i]))];
    }

    private static ExternalPreview ReadPreview(string path, Dictionary<string, ExternalPreview> cache)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return ExternalPreview.Missing;
            }

            lock (cache)
            {
                if (cache.TryGetValue(path, out var cached) && cached.ModifiedUtc == info.LastWriteTimeUtc)
                {
                    return cached;
                }
            }

            // Title and preview come from the start of the file; a huge file is not read whole for them.
            const int MaxBytes = 64 * 1024;
            var bytes = new byte[Math.Min(info.Length, MaxBytes)];
            using (var stream = info.OpenRead())
            {
                stream.ReadExactly(bytes);
            }

            var content = NoteContentAnalyzer.Analyze(DirectoryEngine.Decode(bytes));
            var preview = new ExternalPreview(info.LastWriteTimeUtc, content.Title, content.Preview, IsMissing: false);
            lock (cache)
            {
                cache[path] = preview;
            }

            return preview;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ExternalPreview(default, string.Empty, string.Empty, IsMissing: false);
        }
    }

    /// <summary>
    /// A reload with the same notes in the same order (the usual case after an autosave: the open note is already
    /// on top) only swaps the rows whose summary changed. Clearing and refilling recreated every row on the UI
    /// thread – a ~0.4 s freeze a moment after typing stopped.
    /// </summary>
    private void Replace(List<object> fresh)
    {
        var sameShape = fresh.Count == Items.Count;
        for (var i = 0; sameShape && i < fresh.Count; i++)
        {
            sameShape = (fresh[i], Items[i]) switch
            {
                (HistoryHeaderViewModel a, HistoryHeaderViewModel b) => a.Label == b.Label,
                (HistoryRowViewModel a, HistoryRowViewModel b) => a.Note.RelativePath == b.Note.RelativePath,
                (HistoryExternalRowViewModel a, HistoryExternalRowViewModel b) => a.FullPath == b.FullPath,
                _ => false,
            };
        }

        WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark(sameShape ? "history: rows updated" : $"history: rebuilt {fresh.Count} items");
        if (!sameShape)
        {
            Items.Clear();
            foreach (var entry in fresh)
            {
                Items.Add(entry);
            }

            return;
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            if ((fresh[i], Items[i]) is (HistoryRowViewModel row, HistoryRowViewModel old) && old.Note != row.Note
                || (fresh[i], Items[i]) is (HistoryExternalRowViewModel external, HistoryExternalRowViewModel oldExternal) && !external.SameAs(oldExternal))
            {
                Items[i] = fresh[i];
            }
        }
    }

    private static string HeaderLabel(DateOnly day)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (day == today)
        {
            return Strings.Get(R.History_Today);
        }

        if (day == today.AddDays(-1))
        {
            return Strings.Get(R.History_Yesterday);
        }

        var culture = CultureInfo.CurrentCulture;
        var format = day.Year == today.Year ? "ddd d. MMMM" : "ddd d. MMMM yyyy";
        if (culture.TwoLetterISOLanguageName != "sk")
        {
            format = day.Year == today.Year ? "ddd, MMMM d" : "ddd, MMMM d, yyyy";
        }

        return day.ToString(format, culture);
    }
}

internal sealed partial class HistoryHeaderViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;
}

internal sealed partial class HistoryRowViewModel(NoteSummary note) : ObservableObject
{
    public NoteSummary Note { get; } = note;

    public string TimeText => Note.CreatedAt.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>"VZNIK 9:00" under the edit time; with a short date (no year) when created on another day.</summary>
    public string CreatedText
    {
        get
        {
            var c = CultureInfo.CurrentCulture;
            var created = Note.CreatedAt;
            var time = created.ToString("t", c);
            var when = created.Date == Note.ModifiedAt.Date
                ? time
                : created.ToString(c.TwoLetterISOLanguageName == "sk" ? "d.M." : "M/d", c) + " " + time;
            return Strings.Format(R.History_Created, when).ToUpper(c);
        }
    }

    public string Title => Note.Title.Length > 0 ? Note.Title : Path.GetFileNameWithoutExtension(Note.RelativePath);

    public string TaskText => Note.OpenTasks + Note.DoneTasks == 0 ? string.Empty : $"{Note.DoneTasks}/{Note.OpenTasks + Note.DoneTasks}";

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>An external file in the history: when it was last opened or saved, "external" instead of the creation.</summary>
internal sealed partial class HistoryExternalRowViewModel(string fullPath, DateTime touchedAt, ExternalPreview preview) : ObservableObject
{
    public string FullPath { get; } = fullPath;

    /// <summary>Last open or save, local time – the lead time of the row.</summary>
    public DateTime TouchedAt { get; } = touchedAt;

    public string FileName => Path.GetFileName(FullPath);

    public string Title => Info.Title.Length > 0 ? Info.Title : Path.GetFileNameWithoutExtension(FullPath);

    public string Preview => Info.Preview;

    /// <summary>The file is no longer there (moved or deleted elsewhere).</summary>
    public bool IsMissing => Info.IsMissing;

    public string ExternalText => Strings.Get(R.History_External).ToUpper(CultureInfo.CurrentCulture);

    internal ExternalPreview Info { get; } = preview;

    internal bool SameAs(HistoryExternalRowViewModel other) => TouchedAt == other.TouchedAt && Info == other.Info;
}

/// <summary>Title and preview of an external file as of its modification time.</summary>
internal sealed record ExternalPreview(DateTime ModifiedUtc, string Title, string Preview, bool IsMissing)
{
    public static ExternalPreview Missing { get; } = new(default, string.Empty, string.Empty, IsMissing: true);
}
