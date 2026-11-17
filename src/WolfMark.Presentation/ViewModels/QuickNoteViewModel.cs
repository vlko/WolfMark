namespace WolfMark.App.ViewModels;

/// <summary>
/// The quick note window of both apps (M8-01, ADR 0012): a new note of today, written without the main window. The
/// views only show, hide and place the window.
/// </summary>
internal sealed class QuickNoteViewModel(MainViewModel main)
{
    /// <summary>The window's size in device-independent pixels.</summary>
    public const int Width = 640, Height = 380;

    public NoteEditorViewModel Note { get; } = new(main.Host);

    /// <summary>
    /// The shortcut (or <c>--quick-note</c>) was used. A window already open with text keeps it – a second press
    /// must not throw away what is being written; otherwise a new note starts.
    /// </summary>
    /// <returns>False when the open window only has to come to the front.</returns>
    public async Task<bool> StartAsync(bool windowVisible)
    {
        if (windowVisible && Note.Text.Length > 0)
        {
            return false;
        }

        Note.Mode = main.Host.Settings.Current.DefaultEditorMode;
        await Note.NewAsync(null);
        return true;
    }

    /// <summary>Esc, Ctrl+Enter, the close button, the end of the app: what is written is saved.</summary>
    public Task SaveAsync() => Note.FlushAsync();

    /// <summary>"Continue in the main window": the note there in full view (a note not written yet: a new one there).</summary>
    public async Task OpenInMainAsync()
    {
        await Note.FlushAsync();
        if (Note.IsPersisted && Note.Path is { } path)
        {
            await main.OpenNoteAsync(path.RelativePath);
        }
        else
        {
            await main.NewNoteAsync();
        }

        main.IsFullView = true;
    }

    /// <summary>
    /// Where the window goes on a screen's work area (physical pixels): centred, in the upper third – where the
    /// eyes are; never larger than the work area.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Place(int areaX, int areaY, int areaWidth, int areaHeight, double scale)
    {
        var width = Math.Min((int)(Width * scale), areaWidth);
        var height = Math.Min((int)(Height * scale), areaHeight);
        return (areaX + ((areaWidth - width) / 2), areaY + Math.Max(0, (areaHeight - height) / 3), width, height);
    }
}
