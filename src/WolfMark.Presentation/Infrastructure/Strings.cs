using System.Globalization;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Localised UI strings. The host plugs in its lookup (<see cref="Source"/>: MRT resources on Windows, the
/// generated tables elsewhere); a missing or empty value falls back to English.
/// </summary>
internal static partial class Strings
{
    /// <summary>Key → localised text of the hosting app; null or empty means "use the English fallback".</summary>
    public static Func<string, string?>? Source { get; set; }

    public static string Get(string key)
    {
        string? value = null;
        try
        {
            value = Source?.Invoke(key);
        }
        catch (Exception)
        {
        }

        return string.IsNullOrEmpty(value) ? Fallback.GetValueOrDefault(key, key) : value;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Slovak plural rules (1 / 2–4 / 5+), English uses One/Many.</summary>
    public static string Notes(int count)
    {
        if (count == 0)
        {
            return Get(R.Notes_None);
        }

        var key = count == 1 ? R.Notes_One : count is >= 2 and <= 4 ? R.Notes_Few : R.Notes_Many;
        return Format(key, count);
    }
}
