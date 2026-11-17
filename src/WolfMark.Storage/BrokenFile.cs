namespace WolfMark.Storage;

/// <summary>
/// A settings or tags file that cannot be parsed (a sync merge, a half-written copy) is never just replaced by
/// defaults: a copy stays next to it, so what the user had can be recovered by hand.
/// </summary>
public static class BrokenFile
{
    /// <summary>Copies <paramref name="path"/> to <c>&lt;name&gt;.broken-&lt;time&gt;&lt;ext&gt;</c> in the same folder.</summary>
    /// <returns>The copy, or null when there is no file to keep.</returns>
    public static string? KeepCopy(string path, TimeProvider? time = null)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var stamp = (time ?? TimeProvider.System).GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var copy = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.broken-{stamp}{Path.GetExtension(path)}");
        File.Copy(path, copy, overwrite: true);
        return copy;
    }
}
