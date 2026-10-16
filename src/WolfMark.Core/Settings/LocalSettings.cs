namespace WolfMark.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum BackdropKind
{
    Mica,
    Acrylic,
    None,
}

public enum EditorMode
{
    Raw,
    Formatted,
}

/// <summary>Machine-local settings (<c>%LOCALAPPDATA%\WolfMark\settings.json</c>), not synchronised.</summary>
public sealed class LocalSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Notes root; null = auto-detect (OneDrive → Documents).</summary>
    public string? RootDirectory { get; set; }

    /// <summary>Relative path of the last edited note – opened on start without touching the index.</summary>
    public string? LastNotePath { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>The flat design uses a solid paper colour; Mica/Acrylic are optional.</summary>
    public BackdropKind Backdrop { get; set; } = BackdropKind.None;

    public string EditorFontFamily { get; set; } = "Cascadia Text, Segoe UI Variable Text, Segoe UI";

    public double EditorFontSize { get; set; } = 15;

    public double EditorMaxLineWidth { get; set; } = 760;

    public EditorMode DefaultEditorMode { get; set; } = EditorMode.Formatted;

    /// <summary>"sk-SK", "en-US" or null for system.</summary>
    public string? Language { get; set; }

    public double HistoryPaneHeight { get; set; } = 260;

    /// <summary>Calendar panel shows the full month (true) or only the week band.</summary>
    public bool CalendarExpanded { get; set; }

    public WindowPlacement? Window { get; set; }

    public ClaudeCodeSyncSettings ClaudeCode { get; set; } = new();
}

public sealed class WindowPlacement
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsMaximized { get; set; }
}

public sealed class ClaudeCodeSyncSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Path to the <c>.claude</c> directory; null = <c>%USERPROFILE%\.claude</c>.</summary>
    public string? ClaudeDirectory { get; set; }

    /// <summary>Project paths (cwd prefixes) that are never imported.</summary>
    public List<string> ExcludedProjects { get; set; } = [];

    public bool RedactSecrets { get; set; } = true;

    public int IntervalMinutes { get; set; } = 10;

    public bool WatchInRealTime { get; set; }
}
