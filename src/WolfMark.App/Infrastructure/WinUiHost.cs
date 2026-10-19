using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.Resources;

namespace WolfMark.App.Infrastructure;

/// <summary><see cref="IUiDispatcher"/> on the WinUI <see cref="DispatcherQueue"/>.</summary>
internal sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public bool TryEnqueue(Action action) => queue.TryEnqueue(() => action());
}

/// <summary>Localised strings from <c>Strings/&lt;lang&gt;/Resources.resw</c> (MRT Core) for <see cref="Strings.Source"/>.</summary>
internal static class MrtStrings
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

    public static string? Get(string key) => Loader.Value?.GetString(key);
}
