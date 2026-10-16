using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WolfMark.Sync.ClaudeCode;

/// <summary>
/// Tolerant parser of Claude Code transcripts (<c>~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl</c>, M7-01/M7-02).
/// The format changes between versions: unknown fields are ignored, broken lines skipped, and only
/// prompts typed by a human are returned (no tool results, system-injected or compaction messages).
/// Uses <see cref="JsonDocument"/> – no reflection, AOT-safe.
/// </summary>
public static partial class TranscriptParser
{
    public static async IAsyncEnumerable<ClaudePrompt> ReadAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (TryParseLine(line, out var prompt))
            {
                yield return prompt;
            }
        }
    }

    public static IEnumerable<ClaudePrompt> Parse(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (TryParseLine(line, out var prompt))
            {
                yield return prompt;
            }
        }
    }

    public static bool TryParseLine(string line, out ClaudePrompt prompt)
    {
        prompt = null!;
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "type") != "user"
                || IsTrue(root, "isMeta")
                || IsTrue(root, "isSidechain")
                || IsTrue(root, "isCompactSummary")
                || IsTrue(root, "isVisibleInTranscriptOnly")
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || (GetString(message, "role") is { } role && role != "user"))
            {
                return false;
            }

            if (!TryGetHumanText(message, out var text))
            {
                return false;
            }

            var sessionId = GetString(root, "sessionId");
            var timestampText = GetString(root, "timestamp");
            if (sessionId is null || timestampText is null
                || !DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                return false;
            }

            prompt = new ClaudePrompt(
                sessionId,
                GetString(root, "uuid") ?? string.Empty,
                timestamp,
                GetString(root, "cwd"),
                GetString(root, "gitBranch"),
                text);
            return true;
        }
        catch (JsonException)
        {
            return false; // corrupted line – skip
        }
    }

    internal static bool TryGetHumanText(JsonElement message, out string text)
    {
        text = string.Empty;
        if (!message.TryGetProperty("content", out var content))
        {
            return false;
        }

        string raw;
        if (content.ValueKind == JsonValueKind.String)
        {
            raw = content.GetString() ?? string.Empty;
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                var type = part.ValueKind == JsonValueKind.Object ? GetString(part, "type") : null;
                if (type == "tool_result")
                {
                    return false;
                }

                if (type == "text" && GetString(part, "text") is { } t)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append("\n\n");
                    }

                    sb.Append(t);
                }
            }

            raw = sb.ToString();
        }
        else
        {
            return false;
        }

        return TryNormalizeHumanText(raw, out text);
    }

    /// <summary>Filters system-injected content and turns slash commands / bash input into readable text.</summary>
    internal static bool TryNormalizeHumanText(string raw, out string text)
    {
        text = string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (var prefix in IgnoredPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        // Slash command typed by the user: <command-name>/foo</command-name><command-args>bar</command-args>
        var command = CommandNameRegex().Match(trimmed);
        if (command.Success)
        {
            var args = CommandArgsRegex().Match(trimmed);
            text = (command.Groups[1].Value.Trim() + " " + (args.Success ? args.Groups[1].Value.Trim() : string.Empty)).Trim();
            if (!text.StartsWith('/'))
            {
                text = "/" + text;
            }

            return true;
        }

        var bash = BashInputRegex().Match(trimmed);
        if (bash.Success)
        {
            text = "!" + bash.Groups[1].Value.Trim();
            return true;
        }

        // Strip system reminders injected into the user turn.
        var cleaned = SystemReminderRegex().Replace(trimmed, string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            return false;
        }

        text = cleaned;
        return true;
    }

    private static readonly string[] IgnoredPrefixes =
    [
        "<local-command-stdout>",
        "<local-command-stderr>",
        "<local-command-caveat>",
        "<bash-stdout>",
        "<bash-stderr>",
        "<task-notification>",
        "<user-prompt-submit-hook>",
        "Caveat: The messages below were generated by the user while running local commands",
        "[Request interrupted by user",
        "This session is being continued from a previous conversation",
    ];

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"<command-name>(.*?)</command-name>", RegexOptions.Singleline)]
    private static partial Regex CommandNameRegex();

    [GeneratedRegex(@"<command-args>(.*?)</command-args>", RegexOptions.Singleline)]
    private static partial Regex CommandArgsRegex();

    [GeneratedRegex(@"^<bash-input>(.*?)</bash-input>$", RegexOptions.Singleline)]
    private static partial Regex BashInputRegex();

    [GeneratedRegex(@"<system-reminder>.*?</system-reminder>", RegexOptions.Singleline)]
    private static partial Regex SystemReminderRegex();
}
