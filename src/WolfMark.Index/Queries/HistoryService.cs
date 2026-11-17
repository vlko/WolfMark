using Microsoft.Data.Sqlite;
using WolfMark.Core.Tags;
using WolfMark.Index.Database;

namespace WolfMark.Index.Queries;

/// <summary>Keyset cursor for infinite scrolling (created_at DESC, id DESC).</summary>
/// <summary>Keyset cursor for infinite scrolling (last modification DESC, id DESC).</summary>
public sealed record HistoryCursor(long ModifiedUtcTicks, long Id);

public sealed record HistoryQuery(string? Tag = null, DateOnly? Day = null, HistoryCursor? After = null, int Limit = 50);

public sealed record HistoryPage(IReadOnlyList<NoteSummary> Items, HistoryCursor? Next);

/// <summary>An open task of a note: its line in the file (0-based) and its text without Markdown.</summary>
public sealed record OpenTask(int Line, string Text);

/// <summary>A note and its open tasks, in the order they appear in it.</summary>
public sealed record NoteOpenTasks(NoteSummary Note, IReadOnlyList<OpenTask> Tasks);

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
                    where.Add("(n.file_mtime < $m OR (n.file_mtime = $m AND n.id < $id))");
                    cmd.With("$m", after.ModifiedUtcTicks).With("$id", after.Id);
                }

                cmd.CommandText = $"""
                    SELECT {NoteSummaryReader.Columns} FROM notes n
                    {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty)}
                    ORDER BY n.file_mtime DESC, n.id DESC LIMIT $limit;
                    """;
                cmd.With("$limit", query.Limit + 1);

                var items = NoteSummaryReader.ReadAll(cmd);
                HistoryCursor? next = null;
                if (items.Count > query.Limit)
                {
                    items.RemoveAt(items.Count - 1);
                    var last = items[^1];
                    next = new HistoryCursor(last.ModifiedUtcTicks, last.Id);
                }

                return new HistoryPage(items, next);
            },
            ct);

    public Task<int> CountAsync(CancellationToken ct) =>
        db.ReadAsync(
            c =>
            {
                using var cmd = c.Command("SELECT count(*) FROM notes;");
                return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            },
            ct);

    public Task<NoteSummary?> GetAsync(string relativePath, CancellationToken ct) =>
        QuerySingleAsync($"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.rel_path = $p;", cmd => cmd.With("$p", relativePath), ct);

    public Task<NoteSummary?> GetLatestAsync(CancellationToken ct) =>
        QuerySingleAsync($"SELECT {NoteSummaryReader.Columns} FROM notes n ORDER BY n.file_mtime DESC, n.id DESC LIMIT 1;", _ => { }, ct);

    public Task<NoteSummary?> GetLatestOnDayAsync(DateOnly day, CancellationToken ct) =>
        QuerySingleAsync(
            $"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.day = $d ORDER BY n.file_mtime DESC, n.id DESC LIMIT 1;",
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

    /// <summary>
    /// Notes with a wiki link to <paramref name="note"/> – by its path (<c>[[2026/11/16/praca_1403]]</c>) or by its
    /// file name alone (<c>[[praca_1403]]</c>) – the newest first; the note itself is left out (M9-04).
    /// </summary>
    public Task<IReadOnlyList<NoteSummary>> GetBacklinksAsync(Core.Notes.NotePath note, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<NoteSummary>>(
            c =>
            {
                var keys = Core.Markdown.WikiLinks.KeysOf(note);
                using var cmd = c.Command(
                    $"""
                    SELECT {NoteSummaryReader.Columns} FROM notes n
                    WHERE n.rel_path <> $self
                      AND EXISTS (SELECT 1 FROM note_links l WHERE l.note_id = n.id AND l.target IN ($path, $name))
                    ORDER BY n.created_at DESC LIMIT 100;
                    """)
                    .With("$self", note.RelativePath).With("$path", keys[0]).With("$name", keys[1]);
                return NoteSummaryReader.ReadAll(cmd);
            },
            ct);

    /// <summary>The newest note whose file is named <paramref name="name"/> (without ".md"), for <c>[[name]]</c> links.</summary>
    public Task<string?> FindByFileNameAsync(string name, CancellationToken ct) =>
        db.ReadAsync(
            c =>
            {
                using var cmd = c.Command(
                    "SELECT rel_path FROM notes WHERE rel_path = $n OR rel_path LIKE $p ESCAPE '\\' ORDER BY created_at DESC LIMIT 1;")
                    .With("$n", name + ".md")
                    .With("$p", "%/" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + ".md");
                return cmd.ExecuteScalar() as string;
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

    /// <summary>
    /// Notes with open tasks and those tasks, across days ("Open tasks", M9-01): the newest note first – by its
    /// creation, not the last edit, so that ticking a task off does not reorder the list. Narrowed by the tag and
    /// the day like the history.
    /// </summary>
    public Task<IReadOnlyList<NoteOpenTasks>> GetOpenTasksAsync(string? tag, DateOnly? day, int limit, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<NoteOpenTasks>>(
            c =>
            {
                var where = "n.open_tasks > 0";
                using var cmd = c.CreateCommand();
                if (tag is not null)
                {
                    where += " AND EXISTS (SELECT 1 FROM note_tags t WHERE t.note_id = n.id AND t.tag_key = $tag)";
                    cmd.With("$tag", TagName.ToKey(tag));
                }

                if (day is { } d)
                {
                    where += " AND n.day = $day";
                    cmd.With("$day", SqliteExtensions.FormatDay(d));
                }

                cmd.CommandText = $"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE {where} ORDER BY n.created_at DESC, n.id DESC LIMIT $l;";
                cmd.With("$l", limit);
                var notes = NoteSummaryReader.ReadAll(cmd);

                var result = new List<NoteOpenTasks>(notes.Count);
                using var tasks = c.Command("SELECT line, text FROM note_tasks WHERE note_id = $id ORDER BY line;");
                var id = tasks.CreateParameter();
                id.ParameterName = "$id";
                tasks.Parameters.Add(id);
                foreach (var note in notes)
                {
                    id.Value = note.Id;
                    var list = new List<OpenTask>();
                    using var r = tasks.ExecuteReader();
                    while (r.Read())
                    {
                        list.Add(new OpenTask(r.GetInt32(0), r.GetString(1)));
                    }

                    result.Add(new NoteOpenTasks(note, list));
                }

                return result;
            },
            ct);

    /// <summary>Open tasks in all notes – the number next to "Open tasks".</summary>
    public Task<int> CountOpenTasksAsync(CancellationToken ct) =>
        db.ReadAsync(
            c =>
            {
                using var cmd = c.Command("SELECT COALESCE(SUM(open_tasks), 0) FROM notes;");
                return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
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
