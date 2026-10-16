using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Core.Text;

namespace WolfMark.App.ViewModels;

public enum SaveState
{
    New,
    Saved,
    Pending,
    Saving,
    Error,
    ReadOnly,
}

/// <summary>
/// The open note (M4-05, M4-08, M4-09): autosave with 800 ms debounce, flush on focus loss / switch /
/// close, lazy file creation on the first character, tag change = rename, external change handling.
/// </summary>
internal sealed partial class NoteEditorViewModel : ObservableObject
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly AppHost _host;
    private readonly ILogger _logger;
    private readonly Debouncer _autosave = new(AutosaveDelay);
    private readonly Debouncer _settingsSave = new(TimeSpan.FromSeconds(2));
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _dirty;
    private bool _useCrLf;

    public NoteEditorViewModel(AppHost host)
    {
        _host = host;
        _logger = host.Loggers.CreateLogger<NoteEditorViewModel>();
        Mode = host.Settings.Current.DefaultEditorMode;
    }

    /// <summary>Raised when the editor must show new content (open, reload).</summary>
    public event EventHandler<LoadRequest>? LoadRequested;

    /// <summary>Raised after the note got a (new) path – history highlights it.</summary>
    public event EventHandler? PathChanged;

    [ObservableProperty]
    public partial NotePath? Path { get; private set; }

    [ObservableProperty]
    public partial bool IsPersisted { get; private set; }

    [ObservableProperty]
    public partial string? Tag { get; private set; }

    [ObservableProperty]
    public partial DateTime CreatedAt { get; private set; }

    /// <summary>Last modification (file mtime, or the last save); null for a note not yet created.</summary>
    [ObservableProperty]
    public partial DateTime? ModifiedAt { get; private set; }

    [ObservableProperty]
    public partial SaveState SaveState { get; private set; } = SaveState.New;

    [ObservableProperty]
    public partial bool IsReadOnly { get; private set; }

    [ObservableProperty]
    public partial bool IsClaudeCode { get; private set; }

    [ObservableProperty]
    public partial bool IsNonStandard { get; private set; }

    [ObservableProperty]
    public partial NotePath? ConflictOriginal { get; private set; }

    [ObservableProperty]
    public partial bool ExternalChangePending { get; private set; }

    [ObservableProperty]
    public partial bool IsDeletedExternally { get; private set; }

    [ObservableProperty]
    public partial EditorMode Mode { get; set; }

    public string Text { get; private set; } = string.Empty;

    public DateOnly Day => Path?.Date ?? DateOnly.FromDateTime(CreatedAt);

    public async Task OpenAsync(NotePath path, bool caretToEnd = true)
    {
        await FlushAsync();
        string text;
        try
        {
            text = await Task.Run(() => _host.Engine.ReadAsync(path, _host.ShutdownToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(ex, path.RelativePath);
            return;
        }

        var file = await Task.Run(() => _host.Engine.GetFile(path));
        var created = path.CreatedAt ?? file?.CreatedAtLocal ?? DateTime.Now;
        ModifiedAt = file?.LastWriteTimeUtc.ToLocalTime().DateTime;

        _useCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        Text = text.Replace("\r\n", "\n");
        _dirty = false;
        Path = path;
        IsPersisted = true;
        Tag = path.Tag;
        CreatedAt = created;
        IsNonStandard = !path.IsStandard;
        ConflictOriginal = path.ConflictOriginal;
        IsClaudeCode = IsClaudeCodeNote(path, Text);
        IsReadOnly = IsClaudeCode;
        ExternalChangePending = false;
        IsDeletedExternally = false;
        SaveState = IsReadOnly ? SaveState.ReadOnly : SaveState.Saved;
        LoadRequested?.Invoke(this, new LoadRequest(Text, caretToEnd));
        PathChanged?.Invoke(this, EventArgs.Empty);
        RememberLastNote();
    }

    /// <summary>New note in today's folder; the file is created on the first typed character (M4-09).</summary>
    public async Task NewAsync(string? tag, string initialText = "")
    {
        await FlushAsync();
        var now = DateTime.Now;
        _useCrLf = false;
        Path = _host.Engine.ProposeNewPath(now, tag);
        IsPersisted = false;
        Tag = tag;
        CreatedAt = now;
        ModifiedAt = null;
        IsNonStandard = false;
        ConflictOriginal = null;
        IsClaudeCode = false;
        IsReadOnly = false;
        ExternalChangePending = false;
        IsDeletedExternally = false;
        Text = initialText;
        _dirty = initialText.Length > 0;
        SaveState = SaveState.New;
        LoadRequested?.Invoke(this, new LoadRequest(initialText, true));
        PathChanged?.Invoke(this, EventArgs.Empty);
        if (_dirty)
        {
            await SaveAsync(CancellationToken.None);
        }
    }

    public void OnTextEdited(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        Text = text;
        _dirty = true;
        SaveState = SaveState.Pending;
        _autosave.Schedule(SaveAsync);
    }

    /// <summary>Saves pending changes now (focus loss, note switch, window close).</summary>
    public async Task FlushAsync()
    {
        _autosave.Cancel();
        if (_dirty)
        {
            await SaveAsync(CancellationToken.None);
        }
    }

    /// <remarks>A started save always completes; the token only exists to fit the debouncer signature.</remarks>
    private async Task SaveAsync(CancellationToken ct)
    {
        await _saveLock.WaitAsync(CancellationToken.None);
        try
        {
            if (!_dirty || IsReadOnly || Path is null)
            {
                return;
            }

            var text = Text;
            var content = _useCrLf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
            if (!IsPersisted)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _dirty = false;
                    SaveState = SaveState.New;
                    return; // empty notes never become files
                }

                SaveState = SaveState.Saving;
                var createdAt = CreatedAt;
                var tag = Tag;
                Path = await Task.Run(() => _host.Engine.CreateAsync(createdAt, tag, content, CancellationToken.None), CancellationToken.None);
                IsPersisted = true;
                PathChanged?.Invoke(this, EventArgs.Empty);
                RememberLastNote();
            }
            else
            {
                SaveState = SaveState.Saving;
                var path = Path;
                await Task.Run(() => _host.Engine.SaveAsync(path, content, CancellationToken.None), CancellationToken.None);
            }

            ModifiedAt = DateTime.Now;
            _dirty = !ReferenceEquals(text, Text) && !string.Equals(text, Text, StringComparison.Ordinal);
            IsDeletedExternally = false;
            SaveState = _dirty ? SaveState.Pending : SaveState.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex, Path?.RelativePath ?? "?");
            SaveState = SaveState.Error;
            _autosave.Schedule(async t =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), t);
                await SaveAsync(t);
            });
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task ChangeTagAsync(string? tag)
    {
        if (IsReadOnly || IsNonStandard || Path is null)
        {
            return;
        }

        if (tag is not null && !TagName.TryNormalize(tag, out tag))
        {
            return;
        }

        if (!IsPersisted)
        {
            Tag = tag;
            Path = _host.Engine.ProposeNewPath(CreatedAt, tag);
            return;
        }

        await FlushAsync();
        try
        {
            var path = Path;
            Path = await Task.Run(() => _host.Engine.ChangeTagAsync(path, tag, CancellationToken.None));
            Tag = Path.Tag;
            PathChanged?.Invoke(this, EventArgs.Empty);
            RememberLastNote();
        }
        catch (IOException ex)
        {
            LogSaveFailed(ex, Path.RelativePath);
        }
    }

    public async Task DeleteAsync()
    {
        if (Path is null)
        {
            return;
        }

        _autosave.Cancel();
        _dirty = false;
        if (IsPersisted)
        {
            var path = Path;
            await Task.Run(() => _host.Engine.DeleteAsync(path, CancellationToken.None));
        }

        await NewAsync(null);
    }

    /// <summary>Claude Code notes are read-only; this copies the content into a normal note.</summary>
    public Task CopyToOwnNoteAsync()
    {
        var lines = Text.Split('\n');
        var fm = FrontMatter.Parse(lines);
        var body = string.Join('\n', lines.Skip(fm.LineCount)).TrimStart('\n');
        return NewAsync(null, body);
    }

    /// <summary>Handles changes made outside the app to the open note (M4-08).</summary>
    public async Task OnNoteEventAsync(NoteEvent e)
    {
        if (Path is null || e.Origin != ChangeOrigin.External)
        {
            return;
        }

        switch (e)
        {
            case NoteChanged c when IsSame(c.Path):
                if (_dirty)
                {
                    ExternalChangePending = true;
                }
                else
                {
                    await ReloadFromDiskAsync();
                }

                break;
            case NoteRenamed r when IsSame(r.OldPath):
                Path = r.NewPath;
                Tag = r.NewPath.Tag;
                PathChanged?.Invoke(this, EventArgs.Empty);
                RememberLastNote();
                break;
            case NoteDeleted d when IsSame(d.Path) && IsPersisted:
                IsDeletedExternally = true;
                break;
        }
    }

    public async Task ReloadFromDiskAsync()
    {
        if (Path is { } path)
        {
            _dirty = false;
            _autosave.Cancel();
            await OpenAsync(path, caretToEnd: false);
        }
    }

    /// <summary>Overwrites the external version with the editor content.</summary>
    public async Task KeepMineAsync()
    {
        ExternalChangePending = false;
        _dirty = true;
        await SaveAsync(CancellationToken.None);
    }

    private bool IsSame(NotePath other) => string.Equals(Path?.RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase);

    private static bool IsClaudeCodeNote(NotePath path, string text) =>
        (path.Tag is { } tag && TagName.Comparer.Equals(tag, TagName.ClaudeCode))
        || (text.StartsWith("---", StringComparison.Ordinal)
            && string.Equals(FrontMatter.Parse(text.Split('\n', 12)).Get("source"), "claudecode", StringComparison.OrdinalIgnoreCase));

    private void RememberLastNote()
    {
        if (!IsPersisted || Path is null)
        {
            return;
        }

        _host.Settings.Current.LastNotePath = Path.RelativePath;
        _settingsSave.Schedule(_ => _host.SaveSettingsAsync());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening {Path} failed")]
    private partial void LogOpenFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving {Path} failed – will retry")]
    private partial void LogSaveFailed(Exception ex, string path);
}

internal sealed record LoadRequest(string Text, bool CaretToEnd);
