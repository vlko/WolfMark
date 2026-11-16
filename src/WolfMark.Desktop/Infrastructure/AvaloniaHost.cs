using System.Globalization;
using Avalonia.Threading;
using WolfMark.App.Infrastructure;

namespace WolfMark.Desktop.Infrastructure;

/// <summary><see cref="IUiDispatcher"/> on Avalonia's UI thread.</summary>
internal sealed class AvaloniaDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();

    public bool TryEnqueue(Action action)
    {
        Dispatcher.UIThread.Post(action);
        return true;
    }
}

/// <summary>
/// UI strings for <see cref="Strings.Source"/>: Slovak from the generated table (tools/gen-strings.py) when the UI
/// language is Slovak, otherwise null – English comes from the shared fallback.
/// </summary>
internal static partial class DesktopStrings
{
    public static string? Get(string key) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sk" ? Slovak.GetValueOrDefault(key) : null;
}

