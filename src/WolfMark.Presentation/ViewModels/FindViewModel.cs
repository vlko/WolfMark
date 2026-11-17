using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Markdown;

namespace WolfMark.App.ViewModels;

/// <summary>
/// Find and replace in the open note (Ctrl+F, Ctrl+H), shared by both apps. The view owns the editor: it hands in
/// the text and the selection and applies what comes back – a match to select or an edit to make. The current
/// match is shown as the editor's selection.
/// </summary>
internal sealed partial class FindViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The second row with the replacement (Ctrl+H); never for a read-only note.</summary>
    [ObservableProperty]
    public partial bool ShowReplace { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Replacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    /// <summary>"3 of 12", "No results", "5 replaced" – or empty while nothing is typed.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>Ctrl+F / Ctrl+H: selected text of one line becomes the query.</summary>
    public void Open(bool replace, string? selectedText)
    {
        if (!string.IsNullOrEmpty(selectedText) && !selectedText.Contains('\n', StringComparison.Ordinal))
        {
            Query = selectedText;
        }

        ShowReplace = replace;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Status = string.Empty;
    }

    /// <summary>
    /// While the query is typed: the match at the selection or the next one after it (so the current match stays
    /// put as long as it still fits).
    /// </summary>
    public TextMatch? FindFrom(string text, int selectionStart) => Go(text, selectionStart, backwards: false);

    /// <summary>Enter / F3 and Shift+Enter / Shift+F3: the next or the previous match from the selection, wrapping.</summary>
    public TextMatch? FindNext(string text, int selectionStart, int selectionLength, bool backwards) =>
        Go(text, backwards ? selectionStart : selectionStart + Math.Max(selectionLength, IsMatch(text, selectionStart, selectionLength) ? 1 : 0), backwards);

    /// <summary>
    /// Replaces the selection when it is a match; otherwise there is nothing to replace yet and the view just goes to
    /// the next match.
    /// </summary>
    public TextEdit? ReplaceCurrent(string text, int selectionStart, int selectionLength) =>
        IsMatch(text, selectionStart, selectionLength)
            ? TextSearch.Replace(new TextMatch(selectionStart, selectionLength), Replacement)
            : null;

    /// <summary>All matches in one edit (one undo step); null when there are none.</summary>
    public TextEdit? ReplaceAll(string text)
    {
        var matches = TextSearch.FindAll(text, Query, MatchCase);
        var edit = TextSearch.ReplaceAll(text, matches, Replacement);
        Status = edit is null ? NoResults() : Strings.Format(R.Find_Replaced, matches.Count);
        return edit;
    }

    private TextMatch? Go(string text, int from, bool backwards)
    {
        var matches = TextSearch.FindAll(text, Query, MatchCase);
        var index = TextSearch.IndexFrom(matches, from, backwards);
        if (index < 0)
        {
            Status = NoResults();
            return null;
        }

        Status = Strings.Format(R.Find_Count, index + 1, matches.Count);
        return matches[index];
    }

    private string NoResults() => Query.Length == 0 ? string.Empty : Strings.Get(R.Find_NoResults);

    private bool IsMatch(string text, int start, int length) =>
        length > 0 && length == Query.Length && start >= 0 && start + length <= text.Length
        && string.Compare(text, start, Query, 0, length, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0;
}
