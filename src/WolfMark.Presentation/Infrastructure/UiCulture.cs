using System.Globalization;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// The language and formats chosen in the settings for .NET (both apps, at start): the UI culture follows the language;
/// the formatting culture (day and month names) the language when <c>DateFormatsFromLanguage</c>, otherwise the system
/// region; and with <c>Use24HourTime</c> (the default) times are 24-hour whatever that culture would use.
/// </summary>
internal static class UiCulture
{
    /// <summary>False when nothing changed (system language with its own formats, already 24-hour or 12-hour wanted).</summary>
    public static bool Apply(string? language, bool formatsFromLanguage, bool use24HourTime)
    {
        var changed = false;
        var formatting = CultureInfo.CurrentCulture;
        if (TryGet(language) is { } culture)
        {
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
            changed = true;
            if (formatsFromLanguage)
            {
                formatting = culture;
            }
        }

        if (use24HourTime && formatting.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal))
        {
            formatting = With24HourTime(formatting);
        }

        if (!ReferenceEquals(formatting, CultureInfo.CurrentCulture))
        {
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = formatting;
            changed = true;
        }

        return changed;
    }

    /// <summary>The culture with "HH:mm" / "HH:mm:ss" – the separator and everything else stay its own.</summary>
    internal static CultureInfo With24HourTime(CultureInfo culture)
    {
        var copy = (CultureInfo)culture.Clone();
        var separator = copy.DateTimeFormat.TimeSeparator;
        copy.DateTimeFormat.ShortTimePattern = $"HH{separator}mm";
        copy.DateTimeFormat.LongTimePattern = $"HH{separator}mm{separator}ss";
        return copy;
    }

    private static CultureInfo? TryGet(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
