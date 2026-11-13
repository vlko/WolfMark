using WolfMark.App.Infrastructure;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;
using WolfMark.Storage.Tags;

namespace WolfMark.App.ViewModels;

/// <summary>
/// Settings → Tags (M8-05), shared by both apps: the tags to choose from, the check of the new name, the warning
/// before and the message after the rename. The views only lay it out.
/// </summary>
internal sealed class TagRenameModel(MainViewModel vm)
{
    /// <summary>Tags in note file names with their counts; the reserved Claude Code tag is left out.</summary>
    public IReadOnlyList<TagUsage> Tags { get; private set; } = [];

    public async Task LoadAsync()
    {
        var all = await vm.GetPrimaryTagsAsync();
        Tags = [.. all.Where(t => !TagName.Comparer.Equals(t.Tag, TagName.ClaudeCode))];
    }

    public static string Display(TagUsage t) => $"#{t.Tag}  ({t.Count})";

    /// <summary>The problem with <paramref name="input"/> to show under the field, or null when it can be renamed.</summary>
    public string? Check(TagUsage? old, string input, out string normalized)
    {
        normalized = string.Empty;
        if (old is null || string.IsNullOrWhiteSpace(input))
        {
            return string.Empty; // nothing to say yet, but nothing to rename either
        }

        return TagRenamer.Validate(old.Tag, input.Trim().TrimStart('#'), out normalized) switch
        {
            TagRenamer.Problem.InvalidName => Strings.Get(R.Tag_Invalid),
            TagRenamer.Problem.SameName => Strings.Get(R.Settings_TagsSame),
            TagRenamer.Problem.Reserved => Strings.Get(R.Settings_TagsReserved),
            _ => null,
        };
    }

    /// <summary>The tag <paramref name="newTag"/> merges into, or null for a plain rename (also a change of letter case).</summary>
    public TagUsage? MergeTarget(TagUsage old, string newTag) =>
        TagName.Comparer.Equals(old.Tag, newTag) ? null : Tags.FirstOrDefault(t => TagName.Comparer.Equals(t.Tag, newTag));

    public string Warning(TagUsage old, string newTag) => MergeTarget(old, newTag) is { } target
        ? Strings.Format(R.Settings_TagsMergeWarning, old.Tag, target.Tag, old.Count, target.Count)
        : Strings.Format(R.Settings_TagsRenameWarning, old.Tag, newTag, old.Count);

    public string ConfirmText(TagUsage old, string newTag) =>
        Strings.Get(MergeTarget(old, newTag) is null ? R.Settings_TagsConfirmRename : R.Settings_TagsConfirmMerge);

    /// <summary>Renames (or merges) and returns the message for the page; the tag list is reloaded.</summary>
    public async Task<string> RenameAsync(TagUsage old, string newTag, IProgress<int>? progress)
    {
        // A merge takes the existing spelling of the target (#Praca stays #Praca, not #praca).
        var target = MergeTarget(old, newTag)?.Tag ?? newTag;
        var result = await vm.RenameTagAsync(old.Tag, target, progress);
        await LoadAsync();
        if (result.Error is not { } error)
        {
            return Strings.Format(R.Settings_TagsDone, old.Tag, target, result.Renamed);
        }

        return result.RollbackFailures.Count == 0
            ? Strings.Format(R.Settings_TagsFailed, error.Message)
            : Strings.Format(R.Settings_TagsRollbackFailed, error.Message, string.Join(", ", result.RollbackFailures.Select(p => p.RelativePath)), target);
    }
}
