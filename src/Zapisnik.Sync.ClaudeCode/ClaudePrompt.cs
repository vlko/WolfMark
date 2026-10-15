namespace Zapisnik.Sync.ClaudeCode;

/// <summary>A prompt typed by a human in Claude Code.</summary>
public sealed record ClaudePrompt(
    string SessionId,
    string Uuid,
    DateTimeOffset Timestamp,
    string? Cwd,
    string? GitBranch,
    string Text);
