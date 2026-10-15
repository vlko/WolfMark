using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Zapisnik.App.Infrastructure;
using Zapisnik.Index.Queries;

namespace Zapisnik.App.ViewModels;

/// <summary>History list (M5-01/M5-02): keyset paging, tag/day filters, day group headers.</summary>
internal sealed partial class HistoryViewModel(AppHost host) : ObservableObject
{
    private const int PageSize = 60;

    private HistoryCursor? _next;
    private DateOnly? _lastDay;
    private int _generation;
    private string? _currentPath;

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

            Items.Clear();
            _lastDay = null;
            _next = next;
            Append(items);
            IsEmpty = Items.Count == 0;
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
        foreach (var note in notes)
        {
            if (DayFilter is null && note.Day != _lastDay)
            {
                Items.Add(new HistoryHeaderViewModel(HeaderLabel(note.Day)));
                _lastDay = note.Day;
            }

            Items.Add(new HistoryRowViewModel(note)
            {
                IsCurrent = string.Equals(note.RelativePath, _currentPath, StringComparison.OrdinalIgnoreCase),
            });
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

    public string Title => Note.Title.Length > 0 ? Note.Title : Path.GetFileNameWithoutExtension(Note.RelativePath);

    public string TaskText => Note.OpenTasks + Note.DoneTasks == 0 ? string.Empty : $"{Note.DoneTasks}/{Note.OpenTasks + Note.DoneTasks}";

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
