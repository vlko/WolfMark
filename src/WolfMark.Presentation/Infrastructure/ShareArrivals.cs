using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Tags;

namespace WolfMark.App.Infrastructure;

/// <summary>A note shared from the phone that has just arrived, and what to call it in a notification.</summary>
internal sealed record ShareArrival(NotePath Path, string Title);

/// <summary>
/// What the window shows about shares that arrived since the notice was last closed: the latest one (which
/// "Open" opens) and how many there were.
/// </summary>
internal sealed record ShareNoticeInfo(string RelativePath, string Title, int Count)
{
    public static ShareNoticeInfo After(ShareNoticeInfo? shown, ShareArrival arrival) =>
        new(arrival.Path.RelativePath, arrival.Title, (shown?.Count ?? 0) + 1);

    /// <summary>The title of the one share, or "3 new shares; the latest: …".</summary>
    public string Message => Count == 1 ? Title : Strings.Format(R.Share_NoticeMany, Count, Title);
}

internal static class ShareArrivals
{
    /// <summary>
    /// True for a note with the tag Share that someone else put into the notes folder – the phone through OneDrive
    /// (or another computer that received it). Shares received over the network by this app are reported by the
    /// receiver itself; a note the user tags Share by hand is not an arrival.
    /// </summary>
    public static bool IsArrival(NoteEvent e) =>
        e is NoteCreated { Origin: ChangeOrigin.External, Path: { IsStandard: true, IsConflictCopy: false, Tag: { } tag } }
        && TagName.Comparer.Equals(tag, TagName.Share);
}
