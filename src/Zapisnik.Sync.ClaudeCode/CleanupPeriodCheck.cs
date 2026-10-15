using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zapisnik.Sync.ClaudeCode;

/// <summary>Result of checking <c>cleanupPeriodDays</c> in <c>~/.claude/settings.json</c> (M7-07).</summary>
/// <param name="Days">Effective value (Claude Code default is 30 when unset).</param>
/// <param name="IsExplicit">Whether the value is set in the file.</param>
public sealed record CleanupStatus(int Days, bool IsExplicit)
{
    public const int ClaudeCodeDefault = 30;

    public const int Recommended = 365;

    /// <summary>Transcripts older than this are deleted by Claude Code before we may import them.</summary>
    public bool IsRisky => Days < 90;
}

/// <summary>Claude Code deletes old transcripts; warn and offer to raise the limit.</summary>
public static class CleanupPeriodCheck
{
    public static async Task<CleanupStatus> CheckAsync(string settingsPath, CancellationToken ct)
    {
        if (!File.Exists(settingsPath))
        {
            return new CleanupStatus(CleanupStatus.ClaudeCodeDefault, false);
        }

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var doc = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }, ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("cleanupPeriodDays", out var value)
                && value.TryGetInt32(out var days))
            {
                return new CleanupStatus(days, true);
            }
        }
        catch (JsonException)
        {
        }

        return new CleanupStatus(CleanupStatus.ClaudeCodeDefault, false);
    }

    /// <summary>Sets <c>cleanupPeriodDays</c>, keeping every other setting untouched.</summary>
    public static async Task SetAsync(string settingsPath, int days, CancellationToken ct)
    {
        JsonObject root;
        if (File.Exists(settingsPath))
        {
            await using var stream = File.OpenRead(settingsPath);
            root = await JsonNode.ParseAsync(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }, cancellationToken: ct).ConfigureAwait(false) as JsonObject
                   ?? throw new InvalidDataException("settings.json is not a JSON object.");
        }
        else
        {
            root = [];
        }

        root["cleanupPeriodDays"] = days;
        using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            root.WriteTo(writer);
        }

        await Storage.AtomicFile.WriteAllBytesAsync(settingsPath, buffer.ToArray(), overwrite: true, ct).ConfigureAwait(false);
    }
}
