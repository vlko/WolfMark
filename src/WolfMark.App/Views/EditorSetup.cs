using Microsoft.UI.Xaml;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.Editor;

namespace WolfMark.App.Views;

/// <summary>What every editor of the app shares – the main window's and the external files' – texts and theme.</summary>
internal static class EditorSetup
{
    public static void ConfigureTexts(MarkdownEditor editor)
    {
        editor.PlaceholderText = Strings.Get(R.Editor_Placeholder);
        editor.MenuTexts = new EditorMenuTexts(
            Strings.Get(R.Menu_Cut),
            Strings.Get(R.Menu_Copy),
            Strings.Get(R.Menu_Paste),
            Strings.Get(R.Menu_SelectAll),
            Strings.Get(R.Spell_AddToDictionary),
            Strings.Get(R.Spell_Ignore),
            Strings.Get(R.Spell_NoSuggestions),
            Strings.Get(R.Menu_OpenLink));

        editor.CommandIcon = FormatIcons.CreateForMenu;
        editor.CommandText = command => command switch
        {
            EditorCommand.Paragraph => Strings.Get(R.Format_Paragraph),
            EditorCommand.Heading1 => Strings.Get(R.Format_H1),
            EditorCommand.Heading2 => Strings.Get(R.Format_H2),
            EditorCommand.Heading3 => Strings.Get(R.Format_H3),
            EditorCommand.Quote => Strings.Get(R.Format_Quote),
            EditorCommand.BulletList => Strings.Get(R.Format_Bullet),
            EditorCommand.NumberedList => Strings.Get(R.Format_Numbered),
            EditorCommand.Task => Strings.Get(R.Format_Task),
            EditorCommand.Bold => Strings.Get(R.Format_Bold),
            EditorCommand.Italic => Strings.Get(R.Format_Italic),
            EditorCommand.Strikethrough => Strings.Get(R.Format_Strike),
            EditorCommand.Code => Strings.Get(R.Format_Code),
            EditorCommand.Link => Strings.Get(R.Format_Link),
            EditorCommand.CopyPlainText => Strings.Get(R.Copy_Plain),
            EditorCommand.CopyHtml => Strings.Get(R.Copy_Html),
            _ => command.ToString(),
        };
        editor.DropImageCaption = Strings.Get(R.Editor_DropImage);
        editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
    }

    /// <summary>The editor settings (font, size, emoji, line numbers) in the current light or dark theme.</summary>
    public static void ApplyTheme(MarkdownEditor editor, AppHost host)
    {
        var theme = Palette.CurrentTheme;
        var s = host.Settings.Current;
        Func<string, Windows.UI.Color> tagColor = tag => Palette.ToColor(host.Tags.GetColor(tag));
        var baseTheme = theme == ElementTheme.Dark ? MarkdownTheme.Dark(tagColor) : MarkdownTheme.Light(tagColor);
        // The chosen font first; XAML falls back to the defaults if it was uninstalled.
        var monoFont = string.IsNullOrWhiteSpace(s.EditorMonoFontFamily)
            ? baseTheme.MonoFontFamily
            : s.EditorMonoFontFamily.Trim() + ", " + baseTheme.MonoFontFamily;
        editor.Theme = new MarkdownTheme
        {
            Text = baseTheme.Text,
            Muted = baseTheme.Muted,
            Heading = baseTheme.Heading,
            Link = baseTheme.Link,
            Code = baseTheme.Code,
            CodeBackground = baseTheme.CodeBackground,
            Quote = baseTheme.Quote,
            TaskOpen = baseTheme.TaskOpen,
            TaskDone = baseTheme.TaskDone,
            TagColor = tagColor,
            FontFamily = s.EditorMonospace ? monoFont : s.EditorFontFamily,
            MonoFontFamily = monoFont,
            FontSize = s.EditorFontSize,
            ColorFonts = s.EditorColorEmoji,
        };
        editor.ShowLineNumbers = s.EditorLineNumbers;
    }
}
