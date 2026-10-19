using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Index.Queries;

namespace WolfMark.App.ViewModels;

/// <summary>Command-palette search (M5-03) over hybrid search.</summary>
internal sealed partial class SearchViewModel(AppHost host) : ObservableObject
{
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(120));

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public event EventHandler<NoteSummary>? ResultInvoked;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; private set; } = -1;

    [ObservableProperty]
    public partial bool ShowNoResults { get; private set; }

    partial void OnQueryChanged(string value) => _debounce.Schedule(ct => RunAsync(value, ct));

    public void Reset()
    {
        _debounce.Cancel();
        Query = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        ShowNoResults = false;
    }

    private async Task RunAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            SelectedIndex = -1;
            ShowNoResults = false;
            return;
        }

        await host.DatabaseReady;
        IReadOnlyList<SearchHit> hits;
        try
        {
            hits = await host.Search.SearchAsync(query, 30, ct);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            hits = [];
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        Results.Clear();
        foreach (var hit in hits)
        {
            Results.Add(new SearchResultViewModel(hit));
        }

        ShowNoResults = Results.Count == 0;
        Select(Results.Count > 0 ? 0 : -1);
    }

    public void Move(int delta)
    {
        if (Results.Count > 0)
        {
            Select(Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1));
        }
    }

    public void Select(int index)
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Results[SelectedIndex].IsSelected = false;
        }

        SelectedIndex = index;
        if (index >= 0 && index < Results.Count)
        {
            Results[index].IsSelected = true;
        }
    }

    public void InvokeSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            Invoke(Results[SelectedIndex]);
        }
    }

    public void Invoke(SearchResultViewModel result) => ResultInvoked?.Invoke(this, result.Hit.Note);
}

internal sealed partial class SearchResultViewModel(SearchHit hit) : ObservableObject
{
    public SearchHit Hit { get; } = hit;

    public IReadOnlyList<(string Text, bool Highlight)> TitleSegments { get; } = Split(hit.TitleHighlighted.Length > 0 ? hit.TitleHighlighted : hit.Note.Title);

    public IReadOnlyList<(string Text, bool Highlight)> SnippetSegments { get; } = Split(hit.Snippet.Replace('\n', ' '));

    public string DateText { get; } = hit.Note.CreatedAt.ToString("g", CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Splits text with \u0002…\u0003 markers into (text, highlighted) runs.</summary>
    internal static List<(string, bool)> Split(string text)
    {
        var result = new List<(string, bool)>();
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf(SearchService.HighlightStart, i);
            if (open < 0)
            {
                result.Add((text[i..], false));
                break;
            }

            if (open > i)
            {
                result.Add((text[i..open], false));
            }

            var close = text.IndexOf(SearchService.HighlightEnd, open + 1);
            if (close < 0)
            {
                result.Add((text[(open + 1)..], true));
                break;
            }

            result.Add((text[(open + 1)..close], true));
            i = close + 1;
        }

        return result;
    }
}
