using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Index.Queries;

namespace WolfMark.App.ViewModels;

/// <summary>
/// History list (M5-01/M5-02): ordered by the last edit, keyset paging, tag/day filters, headers per
/// day of the last edit.
/// </summary>
internal sealed partial class HistoryViewModel(AppHost host) : ObservableObject
{
    private const int PageSize = 60;

    private HistoryCursor? _next;
    private DateOnly? _lastDay;
    private int _generation;
    private string? _currentPath;
    private bool _loaded;

    /// <summary><see cref="HistoryHeaderViewModel"/> and <see cref="HistoryRowViewModel"/> items.</summary>
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
            Replace([.. Entries(items)]);
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
            Append(page.Items);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Append(IReadOnlyList<NoteSummary> notes)
    {
        foreach (var entry in Entries(notes))
        {
            Items.Add(entry);
        }
    }

    /// <summary>Headers and rows for <paramref name="notes"/>, continuing after <see cref="_lastDay"/>.</summary>
    private IEnumerable<object> Entries(IReadOnlyList<NoteSummary> notes)
    {
        foreach (var note in notes)
        {
            // History is ordered by the last edit, so it is also grouped by the day of the last edit.
            var editedDay = DateOnly.FromDateTime(note.ModifiedAt);
            if (DayFilter is null && editedDay != _lastDay)
            {
                yield return new HistoryHeaderViewModel(HeaderLabel(editedDay));
                _lastDay = editedDay;
            }

            yield return new HistoryRowViewModel(note)
            {
                IsCurrent = string.Equals(note.RelativePath, _currentPath, StringComparison.OrdinalIgnoreCase),
            };
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
            if (fresh[i] is HistoryRowViewModel row && Items[i] is HistoryRowViewModel old && old.Note != row.Note)
            {
                Items[i] = row;
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
