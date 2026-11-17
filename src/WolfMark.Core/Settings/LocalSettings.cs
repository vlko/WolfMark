namespace WolfMark.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
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

    public string EditorFontFamily { get; set; } = "Cascadia Text, Segoe UI Variable Text, Segoe UI";

    /// <summary>The editor uses the monospaced code font instead of <see cref="EditorFontFamily"/>.</summary>
    public bool EditorMonospace { get; set; }

    /// <summary>Monospaced font: inline code and code blocks, and all text with <see cref="EditorMonospace"/>.</summary>
    public string EditorMonoFontFamily { get; set; } = "Cascadia Mono";

    /// <summary>A number before each line of the note.</summary>
    public bool EditorLineNumbers { get; set; } = true;

    public const double EditorFontSizeDefault = 15, EditorFontSizeMin = 10, EditorFontSizeMax = 28;

    /// <summary>Base text size; also changed with Ctrl + / Ctrl − / Ctrl 0 in the editor.</summary>
    public double EditorFontSize { get; set; } = EditorFontSizeDefault;

    /// <summary>The font size <paramref name="steps"/> whole sizes away (+ larger, − smaller), within the limits.</summary>
    public static double ZoomFontSize(double size, int steps) =>
        Math.Clamp(Math.Round(size) + steps, EditorFontSizeMin, EditorFontSizeMax);

    /// <summary>
    /// Lines wrap at about this many characters and the text is centred in a wider window (readability);
    /// 0 = the lines span the whole editor.
    /// </summary>
    public int EditorMaxLineLength { get; set; }

    /// <summary>Choices offered in the settings for <see cref="EditorMaxLineLength"/>.</summary>
    public static IReadOnlyList<int> EditorMaxLineLengthChoices { get; } = [0, 60, 80, 100, 120];

    /// <summary>Emoji drawn in colour (Windows editor) instead of in the text colour.</summary>
    public bool EditorColorEmoji { get; set; } = true;

    public EditorMode DefaultEditorMode { get; set; } = EditorMode.Formatted;

    /// <summary>Proofing (spell check) in the editor, toggled from the toolbar.</summary>
    public bool SpellCheckEnabled { get; set; } = true;

    /// <summary>"sk-SK", "en-US" or null for system.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Day/month names and the time format in the app's <see cref="Language"/>; false = as in the system's region
    /// settings. Only matters when a language is chosen.
    /// </summary>
    public bool DateFormatsFromLanguage { get; set; } = true;

    /// <summary>Times as 13:45 whatever the language or region would use (default); false = their own (1:45 PM).</summary>
    public bool Use24HourTime { get; set; } = true;

    public double HistoryPaneHeight { get; set; } = 260;

    /// <summary>App icon variant (Assets/IconProposals id, older files may carry a number prefix); null = default.</summary>
    public string? AppIcon { get; set; }

    /// <summary>Closing the window hides it to the notification area; the app ends from the tray menu.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>
    /// Days a deleted note stays in the trash before it is removed; 0 = it stays until the user empties the trash
    /// (the default – the app removes nothing on its own unless told to).
    /// </summary>
    public int TrashRetentionDays { get; set; }

    /// <summary>The retention periods offered in Settings → Trash; 0 = keep.</summary>
    public static IReadOnlyList<int> TrashRetentionChoices { get; } = [0, 30, 90, 365];

    /// <summary>Global shortcut for the quick note window (M8-01, Windows); off by default.</summary>
    public QuickNoteSettings QuickNote { get; set; } = new();

    /// <summary>Calendar panel shows the full month (true) or only the week band.</summary>
    public bool CalendarExpanded { get; set; }

    public WindowPlacement? Window { get; set; }

    /// <summary>
    /// Markdown files opened from outside the notes tree (file association, command line), newest first – shown in
    /// the history beside the notes. Kept here, not in the index: they are not part of the notes tree.
    /// </summary>
    public List<ExternalFileEntry> ExternalFiles { get; set; } = [];

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

/// <summary>An external Markdown file in the history: its full path and when it was last opened or saved.</summary>
public sealed class ExternalFileEntry
{
    public string Path { get; set; } = string.Empty;

    public DateTime TouchedUtc { get; set; }
}

/// <summary>The quick note window (M8-01): a small window with a new note, opened by a global shortcut.</summary>
public sealed class QuickNoteSettings
{
    /// <summary>The shortcut is registered while the app runs (also hidden in the notification area).</summary>
    public bool HotkeyEnabled { get; set; }

    /// <summary><see cref="KeyChord"/> text; an invalid value falls back to <see cref="KeyChord.QuickNoteDefault"/>.</summary>
    public string Hotkey { get; set; } = KeyChord.QuickNoteDefault.ToString();

    [System.Text.Json.Serialization.JsonIgnore]
    public KeyChord Chord => KeyChord.TryParse(Hotkey, out var chord) ? chord : KeyChord.QuickNoteDefault;
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
