using System.Globalization;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// The language chosen in the settings for .NET (both apps, at start): the UI culture always, the formatting culture
/// (day and month names, 12/24-hour time) only when <c>DateFormatsFromLanguage</c> – otherwise the system region stays.
/// </summary>
internal static class UiCulture
{
    /// <summary>False when nothing changed (system language, unknown culture).</summary>
    public static bool Apply(string? language, bool formatsFromLanguage)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (formatsFromLanguage)
        {
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
        }

        return true;
    }
}
