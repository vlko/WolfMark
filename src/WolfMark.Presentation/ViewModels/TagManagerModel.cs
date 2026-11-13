using WolfMark.App.Infrastructure;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;
using WolfMark.Storage.Tags;

namespace WolfMark.App.ViewModels;

/// <summary>
/// Settings → Tags (M8-05), shared by both apps: the list of tags, adding one, and per tag its colour, calendar
/// labels, rename (or merge) and removal – the checks, warnings and messages. The views only lay it out.
/// </summary>
internal sealed class TagManagerModel(MainViewModel vm)
{
    private AppHost Host => vm.Host;

    /// <summary>Tags of notes (most used first), then tags without notes; see <see cref="AppHost.GetTagListAsync"/>.</summary>
    public IReadOnlyList<TagUsage> Tags { get; private set; } = [];

    public async Task LoadAsync() => Tags = await Host.GetTagListAsync(Host.ShutdownToken);

    public string CountText(TagUsage t) =>
        t.Count == 0 ? Strings.Get(R.Settings_TagsUnused) : t.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The Claude Code tag keeps its name (its notes belong to the sync); its colour can change.</summary>
    public static bool IsReserved(TagUsage t) => TagName.Comparer.Equals(t.Tag, TagName.ClaudeCode);

    public TagColor ColorOf(TagUsage t) => Host.Tags.GetColor(t.Tag);

    public bool ShowsLabels(TagUsage t) => Host.Tags.ShowsInCalendarLabels(t.Tag);

    public Task SetColorAsync(TagUsage t, TagColor color) => Task.Run(() => Host.Tags.SetColorAsync(t.Tag, color, CancellationToken.None));

    public Task SetLabelsAsync(TagUsage t, bool show) => Task.Run(() => Host.Tags.SetShowInCalendarLabelsAsync(t.Tag, show, CancellationToken.None));

    /// <summary>History filtered to the tag – the main window shows its notes once Settings close.</summary>
    public Task ShowNotesAsync(TagUsage t) => vm.SetActiveTagAsync(t.Tag);

    // ------------------------------------------------------------------ add

    /// <summary>The problem with a new tag name to show under the field ("" = nothing typed), or null when it can be added.</summary>
    public string? CheckNew(string input, out string normalized)
    {
        normalized = string.Empty;
        var text = input.Trim().TrimStart('#');
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (!TagName.TryNormalize(text, out normalized))
        {
            return Strings.Get(R.Tag_Invalid);
        }

        var name = normalized;
        return Tags.FirstOrDefault(t => TagName.Comparer.Equals(t.Tag, name)) is { } existing
            ? Strings.Format(R.Settings_TagsExists, existing.Tag)
            : null;
    }

    public async Task<string> AddAsync(string tag)
    {
        await Task.Run(() => Host.Tags.AddAsync(tag, CancellationToken.None));
        await LoadAsync();
        return Strings.Format(R.Settings_TagsAdded, tag);
    }

    // ------------------------------------------------------------------ rename / merge

    /// <summary>The problem with the new name to show under the field, or null when it can be renamed.</summary>
    public static string? CheckRename(TagUsage old, string input, out string normalized) =>
        TagRenamer.Validate(old.Tag, input.Trim().TrimStart('#'), out normalized) switch
        {
            TagRenamer.Problem.InvalidName => input.Trim().Length == 0 ? string.Empty : Strings.Get(R.Tag_Invalid),
            TagRenamer.Problem.SameName => Strings.Get(R.Settings_TagsSame),
            TagRenamer.Problem.Reserved => Strings.Get(R.Settings_TagsReserved),
            _ => null,
        };

    /// <summary>The tag <paramref name="newTag"/> merges into, or null for a plain rename (also a change of letter case).</summary>
    public TagUsage? MergeTarget(TagUsage old, string newTag) =>
        TagName.Comparer.Equals(old.Tag, newTag) ? null : Tags.FirstOrDefault(t => TagName.Comparer.Equals(t.Tag, newTag));

    public string RenameWarning(TagUsage old, string newTag) => MergeTarget(old, newTag) is { } target
        ? Strings.Format(R.Settings_TagsMergeWarning, old.Tag, target.Tag, old.Count, target.Count)
        : Strings.Format(R.Settings_TagsRenameWarning, old.Tag, newTag, old.Count);

    public string RenameConfirmText(TagUsage old, string newTag) =>
        Strings.Get(MergeTarget(old, newTag) is null ? R.Settings_TagsConfirmRename : R.Settings_TagsConfirmMerge);

    /// <summary>Renames (or merges) and returns the message for the page; the tag list is reloaded.</summary>
    public async Task<string> RenameAsync(TagUsage old, string newTag, IProgress<int>? progress)
    {
        // A merge takes the existing spelling of the target (#Praca stays #Praca, not #praca).
        var target = MergeTarget(old, newTag)?.Tag ?? newTag;
        return await RunAsync(old, target, progress, renamed => Strings.Format(R.Settings_TagsDone, old.Tag, target, renamed));
    }

    // ------------------------------------------------------------------ remove

    public static string DeleteWarning(TagUsage old) => old.Count == 0
        ? Strings.Format(R.Settings_TagsDeleteUnused, old.Tag)
        : Strings.Format(R.Settings_TagsDeleteWarning, old.Tag, old.Count);

    /// <summary>The notes lose the tag (they stay), its colour is forgotten; returns the message for the page.</summary>
    public Task<string> DeleteAsync(TagUsage old, IProgress<int>? progress) =>
        RunAsync(old, null, progress, renamed => Strings.Format(R.Settings_TagsDeleted, old.Tag, renamed));

    private async Task<string> RunAsync(TagUsage old, string? target, IProgress<int>? progress, Func<int, string> done)
    {
        TagRenamer.Result result;
        try
        {
            result = await vm.RenameTagAsync(old.Tag, target, progress);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await LoadAsync();
            return Strings.Format(R.Settings_TagsFailed, ex.Message); // finding the notes failed – nothing renamed
        }

        await LoadAsync();
        if (result.Error is not { } error)
        {
            return done(result.Renamed);
        }

        return result.RollbackFailures.Count == 0
            ? Strings.Format(R.Settings_TagsFailed, error.Message)
            : Strings.Format(R.Settings_TagsRollbackFailed, error.Message, string.Join(", ", result.RollbackFailures.Select(p => p.RelativePath)), target ?? "-");
    }
}
