using System.Globalization;
using System.Text;

namespace WolfMark.Sync.ClaudeCode;

/// <summary>
/// Renders one note per session and local day (SPEC §4.7, M7-03). Deterministic output, so re-running
/// the sync over the same transcript produces the same file (idempotency without extra state).
/// </summary>
public static class SessionNoteRenderer
{
    public static string Render(string sessionId, IReadOnlyList<(DateTime LocalTime, ClaudePrompt Prompt)> prompts)
    {
        var first = prompts[0].Prompt;
        var cwd = prompts.Select(p => p.Prompt.Cwd).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        var branch = prompts.Select(p => p.Prompt.GitBranch).LastOrDefault(b => !string.IsNullOrEmpty(b));
        var project = ProjectName(cwd);

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("source: claudecode\n");
        sb.Append("session: ").Append(sessionId).Append('\n');
        if (cwd is not null)
        {
            sb.Append("project: \"").Append(cwd.Replace("\"", "\\\"")).Append("\"\n");
        }

        if (branch is not null)
        {
            sb.Append("branch: ").Append(branch).Append('\n');
        }

        sb.Append("---\n");
        sb.Append("# ").Append(project ?? "Claude Code").Append('\n');
        sb.Append('\n');
        sb.Append("> Claude Code · ").Append(cwd ?? "?");
        if (branch is not null)
        {
            sb.Append(" · `").Append(branch).Append('`');
        }

        sb.Append(" · ").Append(first.SessionId.Length > 8 ? first.SessionId[..8] : first.SessionId).Append('\n');

        foreach (var (time, prompt) in prompts)
        {
            sb.Append('\n');
            sb.Append("### ").Append(time.ToString("HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append('\n');
            sb.Append(prompt.Text.Replace("\r\n", "\n").TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    public static string? ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        var trimmed = cwd.TrimEnd('/', '\\');
        var idx = trimmed.LastIndexOfAny(['/', '\\']);
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }
}
