using WolfMark.Core.Notes;

namespace WolfMark.Core.Events;

/// <summary>Who caused a change: this application (editor/sync) or something outside it.</summary>
public enum ChangeOrigin
{
    Local,
    External,
}

/// <summary>
/// Domain events produced by the directory engine. Closed hierarchy: switch over the sealed records.
/// (C# 15 unions are a natural fit once they ship in the GA compiler – see docs/adr/0002.)
/// </summary>
public abstract record NoteEvent(ChangeOrigin Origin);

public sealed record NoteCreated(NotePath Path, ChangeOrigin Origin) : NoteEvent(Origin);

public sealed record NoteChanged(NotePath Path, ChangeOrigin Origin) : NoteEvent(Origin);

public sealed record NoteRenamed(NotePath OldPath, NotePath NewPath, ChangeOrigin Origin) : NoteEvent(Origin);

public sealed record NoteDeleted(NotePath Path, ChangeOrigin Origin) : NoteEvent(Origin);

/// <summary>The watcher lost events (buffer overflow) or a directory moved: rescan.</summary>
/// <param name="RelativeDirectory">Directory to rescan, or null for the whole tree.</param>
public sealed record RescanRequested(string? RelativeDirectory, ChangeOrigin Origin) : NoteEvent(Origin);

/// <summary><c>.wolfmark/tags.json</c> changed.</summary>
public sealed record TagsChanged(ChangeOrigin Origin) : NoteEvent(Origin);
