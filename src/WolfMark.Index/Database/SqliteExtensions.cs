using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WolfMark.Index.Database;

public static class SqliteExtensions
{
    public static SqliteCommand Command(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        return cmd;
    }

    public static SqliteCommand With(this SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static int Execute(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.Command(sql, transaction);
        return cmd.ExecuteNonQuery();
    }

    public static string? Scalar(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var cmd = connection.Command(sql, transaction);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static string? GetStringOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly ParseDay(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatTimestamp(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static DateTime ParseTimestamp(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None);
}
