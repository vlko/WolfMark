namespace WolfMark.Storage;

/// <summary>A note as read from disk: the decoded text and the hash of the file's bytes at that moment.</summary>
public sealed record NoteSnapshot(string Text, string Hash);
