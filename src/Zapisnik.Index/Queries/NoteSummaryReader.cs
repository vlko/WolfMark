using Microsoft.Data.Sqlite;
using Zapisnik.Index.Database;

namespace Zapisnik.Index.Queries;

internal static class NoteSummaryReader
{
    public const string Columns =
        "n.id, n.rel_path, n.created_at, n.day, n.primary_tag, n.title, n.preview, n.word_count, n.open_tasks, n.done_tasks, n.source, n.is_standard, n.is_conflict, n.is_placeholder";

    public const int ColumnCount = 14;

    public static NoteSummary Read(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        SqliteExtensions.ParseTimestamp(r.GetString(2)),
        SqliteExtensions.ParseDay(r.GetString(3)),
        r.GetStringOrNull(4),
        r.GetString(5),
        r.GetString(6),
        r.GetInt32(7),
        r.GetInt32(8),
        r.GetInt32(9),
        r.GetString(10),
        r.GetInt64(11) != 0,
        r.GetInt64(12) != 0,
        r.GetInt64(13) != 0);

    public static List<NoteSummary> ReadAll(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<NoteSummary>();
        while (r.Read())
        {
            list.Add(Read(r));
        }

        return list;
    }
}
