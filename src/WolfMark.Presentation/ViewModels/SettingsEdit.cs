using WolfMark.Core.Settings;

namespace WolfMark.App.ViewModels;

/// <summary>
/// What the Settings window of both apps edits, apart from the things only one system has (start with Windows,
/// the global shortcut, file associations). A view fills its controls from <see cref="From"/>, writes them back
/// into the same object and calls <see cref="ApplyTo"/>; a value a view does not show stays as it was.
/// </summary>
internal sealed class SettingsEdit
{
    /// <summary>The languages on offer; null = the system's.</summary>
    public static IReadOnlyList<string?> Languages => LanguageCodes;

    private static readonly string?[] LanguageCodes = [null, "sk-SK", "en-US"];

    public AppTheme Theme { get; set; }

    public string? Language { get; set; }

    public bool DateFormatsFromLanguage { get; set; }

    public bool Use24HourTime { get; set; }

    public bool CloseToTray { get; set; }

    /// <summary>Null = the default icon.</summary>
    public string? AppIcon { get; set; }

    /// <summary>As typed; blank keeps the current font.</summary>
    public string? EditorFontFamily { get; set; }

    public bool EditorMonospace { get; set; }

    public string? EditorMonoFontFamily { get; set; }

    public bool EditorLineNumbers { get; set; }

    public bool EditorColorEmoji { get; set; }

    /// <summary>Null or NaN (an emptied number box) keeps the current size.</summary>
    public double? EditorFontSize { get; set; }

    public int EditorMaxLineLength { get; set; }

    public EditorMode DefaultEditorMode { get; set; }

    public bool ClaudeEnabled { get; set; }

    /// <summary>As typed; blank = the default <c>~/.claude</c>.</summary>
    public string? ClaudeDirectory { get; set; }

    /// <summary>One project path per line, as typed.</summary>
    public string ClaudeExcludedProjects { get; set; } = string.Empty;

    public bool ClaudeRedactSecrets { get; set; }

    public bool ClaudeWatchInRealTime { get; set; }

    /// <summary>Null or NaN keeps the current interval.</summary>
    public double? ClaudeIntervalMinutes { get; set; }

    public static SettingsEdit From(LocalSettings s) => new()
    {
        Theme = s.Theme,
        Language = s.Language,
        DateFormatsFromLanguage = s.DateFormatsFromLanguage,
        Use24HourTime = s.Use24HourTime,
        CloseToTray = s.CloseToTray,
        AppIcon = s.AppIcon,
        EditorFontFamily = s.EditorFontFamily,
        EditorMonospace = s.EditorMonospace,
        EditorMonoFontFamily = s.EditorMonoFontFamily,
        EditorLineNumbers = s.EditorLineNumbers,
        EditorColorEmoji = s.EditorColorEmoji,
        EditorFontSize = s.EditorFontSize,
        EditorMaxLineLength = s.EditorMaxLineLength,
        DefaultEditorMode = s.DefaultEditorMode,
        ClaudeEnabled = s.ClaudeCode.Enabled,
        ClaudeDirectory = s.ClaudeCode.ClaudeDirectory,
        ClaudeExcludedProjects = string.Join(Environment.NewLine, s.ClaudeCode.ExcludedProjects),
        ClaudeRedactSecrets = s.ClaudeCode.RedactSecrets,
        ClaudeWatchInRealTime = s.ClaudeCode.WatchInRealTime,
        ClaudeIntervalMinutes = s.ClaudeCode.IntervalMinutes,
    };

    /// <summary>Index of <paramref name="language"/> in <see cref="Languages"/>; an unknown one counts as the system's.</summary>
    public static int LanguageIndex(string? language) => Math.Max(0, Array.IndexOf(LanguageCodes, language));

    /// <summary>The text widths on offer, plus a value written into settings.json by hand; 0 = the whole window.</summary>
    public static int[] TextWidthChoices(LocalSettings s) =>
        [.. LocalSettings.EditorMaxLineLengthChoices.Append(Math.Max(0, s.EditorMaxLineLength)).Distinct().Order()];

    /// <summary>Writes the edit into <paramref name="s"/>.</summary>
    /// <returns>True when a change only applies after a restart (language, formats, Claude Code sync).</returns>
    public bool ApplyTo(LocalSettings s)
    {
        var cc = s.ClaudeCode;
        var directory = string.IsNullOrWhiteSpace(ClaudeDirectory) ? null : ClaudeDirectory.Trim();
        var excluded = ClaudeExcludedProjects
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var interval = ClaudeIntervalMinutes is { } minutes && !double.IsNaN(minutes)
            ? Math.Clamp((int)minutes, 1, 1440)
            : cc.IntervalMinutes;

        var restart = !string.Equals(Language, s.Language, StringComparison.Ordinal)
                      || s.DateFormatsFromLanguage != DateFormatsFromLanguage
                      || s.Use24HourTime != Use24HourTime
                      || cc.Enabled != ClaudeEnabled
                      || cc.RedactSecrets != ClaudeRedactSecrets
                      || cc.WatchInRealTime != ClaudeWatchInRealTime
                      || cc.IntervalMinutes != interval
                      || !string.Equals(cc.ClaudeDirectory, directory, StringComparison.Ordinal)
                      || !cc.ExcludedProjects.SequenceEqual(excluded);

        s.Theme = Theme;
        s.Language = Language;
        s.DateFormatsFromLanguage = DateFormatsFromLanguage;
        s.Use24HourTime = Use24HourTime;
        s.CloseToTray = CloseToTray;
        s.AppIcon = AppIcon;
        s.EditorFontFamily = string.IsNullOrWhiteSpace(EditorFontFamily) ? s.EditorFontFamily : EditorFontFamily.Trim();
        s.EditorMonospace = EditorMonospace;
        s.EditorMonoFontFamily = string.IsNullOrWhiteSpace(EditorMonoFontFamily) ? s.EditorMonoFontFamily : EditorMonoFontFamily;
        s.EditorLineNumbers = EditorLineNumbers;
        s.EditorColorEmoji = EditorColorEmoji;
        s.EditorFontSize = EditorFontSize is { } size && !double.IsNaN(size)
            ? Math.Clamp(size, LocalSettings.EditorFontSizeMin, LocalSettings.EditorFontSizeMax)
            : s.EditorFontSize;
        s.EditorMaxLineLength = Math.Max(0, EditorMaxLineLength);
        s.DefaultEditorMode = DefaultEditorMode;
        cc.Enabled = ClaudeEnabled;
        cc.ClaudeDirectory = directory;
        cc.ExcludedProjects = excluded;
        cc.RedactSecrets = ClaudeRedactSecrets;
        cc.WatchInRealTime = ClaudeWatchInRealTime;
        cc.IntervalMinutes = interval;
        return restart;
    }
}
