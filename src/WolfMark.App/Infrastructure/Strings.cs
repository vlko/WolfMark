using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace WolfMark.App.Infrastructure;

/// <summary>Localised UI strings from <c>Strings/&lt;lang&gt;/Resources.resw</c> (MRT Core).</summary>
internal static partial class Strings
{
    private static readonly Lazy<ResourceLoader?> Loader = new(() =>
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception)
        {
            return null; // resources.pri missing – fall back to English
        }
    });

    public static string Get(string key)
    {
        string? value = null;
        try
        {
            value = Loader.Value?.GetString(key);
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
