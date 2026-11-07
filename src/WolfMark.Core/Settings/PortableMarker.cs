namespace WolfMark.Core.Settings;

/// <summary>
/// <c>WolfMark.portable.json</c> next to the executable (ADR 0009). Its presence switches the app to portable mode:
/// settings, index and notes live in <see cref="DataDirectory"/> instead of the user profile.
/// </summary>
public sealed class PortableMarker
{
    public int Version { get; set; } = 1;

    /// <summary>The portable data folder; a relative path is relative to the executable's folder.</summary>
    public string? DataDirectory { get; set; }
}
