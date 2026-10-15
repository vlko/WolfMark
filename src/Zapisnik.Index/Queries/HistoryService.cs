using Microsoft.Data.Sqlite;
using Zapisnik.Core.Tags;
using Zapisnik.Index.Database;

namespace Zapisnik.Index.Queries;

/// <summary>Keyset cursor for infinite scrolling (created_at DESC, id DESC).</summary>
public sealed record HistoryCursor(DateTime CreatedAt, long Id);

public sealed record HistoryQuery(string? Tag = null, DateOnly? Day = null, HistoryCursor? After = null, int Limit = 50);

public sealed record HistoryPage(IReadOnlyList<NoteSummary> Items, HistoryCursor? Next);

/// <summary>History list and single-note lookups (M5-01, M5-02).</summary>
public sealed class HistoryService(IndexDatabase db)
{
    public Task<HistoryPage> GetPageAsync(HistoryQuery query, CancellationToken ct) =>
        db.ReadAsync(
            c =>
            {
                var where = new List<string>();
                using var cmd = c.CreateCommand();
                if (query.Tag is { } tag)
                {
                    where.Add("EXISTS (SELECT 1 FROM note_tags t WHERE t.note_id = n.id AND t.tag_key = $tag)");
                    cmd.With("$tag", TagName.ToKey(tag));
                }

                if (query.Day is { } day)
                {
                    where.Add("n.day = $day");
                    cmd.With("$day", SqliteExtensions.FormatDay(day));
                }

                if (query.After is { } after)
                {
                    where.Add("(n.created_at < $ca OR (n.created_at = $ca AND n.id < $id))");
                    cmd.With("$ca", SqliteExtensions.FormatTimestamp(after.CreatedAt)).With("$id", after.Id);
                }

                cmd.CommandText = $"""
                    SELECT {NoteSummaryReader.Columns} FROM notes n
                    {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
                    ORDER BY n.created_at DESC, n.id DESC LIMIT $limit;
                    """;
                cmd.With("$limit", query.Limit + 1);

                var items = NoteSummaryReader.ReadAll(cmd);
                HistoryCursor? next = null;
                if (items.Count > query.Limit)
                {
                    items.RemoveAt(items.Count - 1);
                    var last = items[^1];
                    next = new HistoryCursor(last.CreatedAt, last.Id);
                }

                return new HistoryPage(items, next);
            },
            ct);

    public Task<NoteSummary?> GetAsync(string relativePath, CancellationToken ct) =>
        QuerySingleAsync($"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.rel_path = $p;", cmd => cmd.With("$p", relativePath), ct);

    public Task<NoteSummary?> GetLatestAsync(CancellationToken ct) =>
        QuerySingleAsync($"SELECT {NoteSummaryReader.Columns} FROM notes n ORDER BY n.created_at DESC, n.id DESC LIMIT 1;", _ => { }, ct);

    public Task<NoteSummary?> GetLatestOnDayAsync(DateOnly day, CancellationToken ct) =>
        QuerySingleAsync(
            $"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.day = $d ORDER BY n.created_at DESC, n.id DESC LIMIT 1;",
            cmd => cmd.With("$d", SqliteExtensions.FormatDay(day)),
            ct);

    public Task<IReadOnlyList<NoteSummary>> GetDayAsync(DateOnly day, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<NoteSummary>>(
            c =>
            {
                using var cmd = c.Command($"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.day = $d ORDER BY n.created_at;")
                    .With("$d", SqliteExtensions.FormatDay(day));
                return NoteSummaryReader.ReadAll(cmd);
            },
            ct);

    public Task<IReadOnlyList<NoteSummary>> GetConflictsAsync(CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<NoteSummary>>(
            c =>
            {
                using var cmd = c.Command($"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.is_conflict = 1 ORDER BY n.created_at DESC;");
                return NoteSummaryReader.ReadAll(cmd);
            },
            ct);

    /// <summary>All tags ever used (primary and in-text), for the tag picker.</summary>
    public Task<IReadOnlyList<TagUsage>> GetAllTagsAsync(CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<TagUsage>>(
            c =>
            {
                using var cmd = c.Command("SELECT MIN(tag), COUNT(*) FROM note_tags GROUP BY tag_key ORDER BY COUNT(*) DESC, MIN(tag);");
                using var r = cmd.ExecuteReader();
                var list = new List<TagUsage>();
                while (r.Read())
                {
                    list.Add(new TagUsage(r.GetString(0), r.GetInt32(1)));
                }

                return list;
            },
            ct);

    /// <summary>Notes with open tasks across days ("Outstanding", M9-01).</summary>
    public Task<IReadOnlyList<NoteSummary>> GetWithOpenTasksAsync(int limit, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<NoteSummary>>(
            c =>
            {
                using var cmd = c.Command($"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.open_tasks > 0 ORDER BY n.created_at DESC LIMIT $l;")
                    .With("$l", limit);
                return NoteSummaryReader.ReadAll(cmd);
            },
            ct);

    private Task<NoteSummary?> QuerySingleAsync(string sql, Action<SqliteCommand> bind, CancellationToken ct) =>
        db.ReadAsync(
            c =>
            {
                using var cmd = c.Command(sql);
                bind(cmd);
                using var r = cmd.ExecuteReader();
                return r.Read() ? NoteSummaryReader.Read(r) : null;
            },
            ct);
}
