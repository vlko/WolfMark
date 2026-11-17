using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;

namespace WolfMark.Presentation.Tests;

/// <summary>The part of Settings both apps share: what is written, and which changes need a restart.</summary>
public sealed class SettingsEditTests
{
    [Fact]
    public void An_untouched_edit_changes_nothing_and_needs_no_restart()
    {
        var s = new LocalSettings { Language = "sk-SK", EditorFontSize = 17, AppIcon = "white" };
        s.ClaudeCode.ExcludedProjects = ["/work/secret"];
        s.ClaudeCode.IntervalMinutes = 25;
        var before = System.Text.Json.JsonSerializer.Serialize(s, WolfMarkJsonContext.Default.LocalSettings);

        var restart = SettingsEdit.From(s).ApplyTo(s);

        Assert.False(restart);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(s, WolfMarkJsonContext.Default.LocalSettings));
    }

    [Fact]
    public void Appearance_and_editor_changes_apply_without_a_restart()
    {
        var s = new LocalSettings();
        var edit = SettingsEdit.From(s);
        edit.Theme = AppTheme.Dark;
        edit.CloseToTray = true;
        edit.EditorFontFamily = "  Georgia ";
        edit.EditorMonospace = true;
        edit.EditorLineNumbers = false;
        edit.EditorColorEmoji = false;
        edit.EditorFontSize = 18;
        edit.EditorMaxLineLength = 80;
        edit.DefaultEditorMode = EditorMode.Raw;

        Assert.False(edit.ApplyTo(s));

        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.True(s.CloseToTray);
        Assert.Equal("Georgia", s.EditorFontFamily);
        Assert.True(s.EditorMonospace);
        Assert.False(s.EditorLineNumbers);
        Assert.False(s.EditorColorEmoji);
        Assert.Equal(18, s.EditorFontSize);
        Assert.Equal(80, s.EditorMaxLineLength);
        Assert.Equal(EditorMode.Raw, s.DefaultEditorMode);
    }

    [Fact]
    public void Blank_or_emptied_fields_keep_the_current_values()
    {
        var s = new LocalSettings { EditorFontFamily = "Georgia", EditorFontSize = 17, EditorMonoFontFamily = "Consolas" };
        s.ClaudeCode.IntervalMinutes = 25;
        var edit = SettingsEdit.From(s);
        edit.EditorFontFamily = "   ";
        edit.EditorMonoFontFamily = null;
        edit.EditorFontSize = double.NaN; // an emptied number box
        edit.ClaudeIntervalMinutes = null;

        Assert.False(edit.ApplyTo(s));

        Assert.Equal("Georgia", s.EditorFontFamily);
        Assert.Equal("Consolas", s.EditorMonoFontFamily);
        Assert.Equal(17, s.EditorFontSize);
        Assert.Equal(25, s.ClaudeCode.IntervalMinutes);
    }

    [Fact]
    public void Sizes_out_of_range_are_clamped()
    {
        var s = new LocalSettings();
        var edit = SettingsEdit.From(s);
        edit.EditorFontSize = 400;
        edit.ClaudeIntervalMinutes = 0;
        edit.EditorMaxLineLength = -5;

        edit.ApplyTo(s);

        Assert.Equal(LocalSettings.EditorFontSizeMax, s.EditorFontSize);
        Assert.Equal(1, s.ClaudeCode.IntervalMinutes);
        Assert.Equal(0, s.EditorMaxLineLength);
    }

    private static readonly Dictionary<string, Action<SettingsEdit>> RestartChanges = new()
    {
        ["language"] = e => e.Language = "en-US",
        ["date formats"] = e => e.DateFormatsFromLanguage = !e.DateFormatsFromLanguage,
        ["24-hour time"] = e => e.Use24HourTime = !e.Use24HourTime,
        ["sync on/off"] = e => e.ClaudeEnabled = !e.ClaudeEnabled,
        ["redaction"] = e => e.ClaudeRedactSecrets = !e.ClaudeRedactSecrets,
        ["watching"] = e => e.ClaudeWatchInRealTime = !e.ClaudeWatchInRealTime,
        ["interval"] = e => e.ClaudeIntervalMinutes = 30,
        ["folder"] = e => e.ClaudeDirectory = "/other/.claude",
        ["excluded projects"] = e => e.ClaudeExcludedProjects = "/work/secret",
    };

    [Theory]
    [InlineData("language")]
    [InlineData("date formats")]
    [InlineData("24-hour time")]
    [InlineData("sync on/off")]
    [InlineData("redaction")]
    [InlineData("watching")]
    [InlineData("interval")]
    [InlineData("folder")]
    [InlineData("excluded projects")]
    public void Language_formats_and_sync_changes_need_a_restart(string what)
    {
        var s = new LocalSettings();
        var edit = SettingsEdit.From(s);
        RestartChanges[what](edit);

        Assert.True(edit.ApplyTo(s), what);
        Assert.False(SettingsEdit.From(s).ApplyTo(s), what + " (applied twice)");
    }

    [Fact]
    public void Excluded_projects_are_one_per_line_without_blanks()
    {
        var s = new LocalSettings();
        var edit = SettingsEdit.From(s);
        edit.ClaudeExcludedProjects = "  /work/secret \r\n\r\n/work/other\n";
        edit.ClaudeDirectory = "  ";

        edit.ApplyTo(s);

        Assert.Equal(["/work/secret", "/work/other"], s.ClaudeCode.ExcludedProjects);
        Assert.Null(s.ClaudeCode.ClaudeDirectory);
    }

    [Fact]
    public void Text_widths_offer_a_hand_written_value_too()
    {
        Assert.Equal([0, 60, 80, 100, 120], SettingsEdit.TextWidthChoices(new LocalSettings()));
        Assert.Equal([0, 60, 72, 80, 100, 120], SettingsEdit.TextWidthChoices(new LocalSettings { EditorMaxLineLength = 72 }));
        Assert.Equal(2, SettingsEdit.LanguageIndex("en-US"));
        Assert.Equal(0, SettingsEdit.LanguageIndex("de-DE"));
    }
}
