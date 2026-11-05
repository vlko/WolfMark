using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Notes;
using WolfMark.Core.Settings;
using WolfMark.Storage;

namespace WolfMark.App.ViewModels;

/// <summary>
/// A Markdown file outside the notes tree in its own window. Unlike notes it is not saved on its own: Save is
/// enabled once the text differs from the file, and closing with changes asks. A change on disk reloads an
/// unchanged editor and asks when there are edits. The file can be copied into today's notes.
/// </summary>
internal sealed partial class ExternalDocumentViewModel : ObservableObject, IDisposable
{
    private readonly AppHost _host;
    private readonly Action<string> _touched;
    private readonly ILogger _logger;
    private readonly Debouncer _wordCount = new(TimeSpan.FromMilliseconds(400));
    private readonly Debouncer _diskChange = new(TimeSpan.FromMilliseconds(300));
    private ExternalDocument? _document;
    private FileSystemWatcher? _watcher;

    /// <param name="touched">Called with the path after every open and save – the history moves it to the top.</param>
    public ExternalDocumentViewModel(AppHost host, string fullPath, Action<string> touched)
    {
        _host = host;
        _touched = touched;
        _logger = host.Loggers.CreateLogger<ExternalDocumentViewModel>();
        FullPath = fullPath;
        Mode = host.Settings.Current.DefaultEditorMode;
    }

    /// <summary>Raised when the editor must show the file's content (open, reload).</summary>
    public event EventHandler<LoadRequest>? LoadRequested;

    public string FullPath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public string DirectoryName => Path.GetDirectoryName(FullPath) ?? string.Empty;

    public string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial DateTime? CreatedAt { get; private set; }

    [ObservableProperty]
    public partial DateTime? ModifiedAt { get; private set; }

    [ObservableProperty]
    public partial int WordCount { get; private set; }

    /// <summary>"UTF-8 · CRLF" – how the file is stored; saving keeps it.</summary>
    [ObservableProperty]
    public partial string EncodingText { get; private set; } = string.Empty;

    /// <summary>The file changed on disk while the editor has changes: reload or keep mine.</summary>
    [ObservableProperty]
    public partial bool ChangedOnDisk { get; private set; }

    [ObservableProperty]
    public partial bool IsDeletedOnDisk { get; private set; }

    /// <summary>Notice text after a failed open or save, null = none.</summary>
    [ObservableProperty]
    public partial string? ErrorNotice { get; set; }

    [ObservableProperty]
    public partial EditorMode Mode { get; set; }

    /// <summary>"Created 3. 11. 2026 14:02 · Modified 5. 11. 10:15 · 1 234 words · UTF-8 · CRLF".</summary>
    public string StatusText
    {
        get
        {
            var c = CultureInfo.CurrentCulture;
            var parts = new List<string>();
            if (CreatedAt is { } created)
            {
                parts.Add(Strings.Format(R.ExtFile_Created, created.ToString("g", c)));
            }

            if (ModifiedAt is { } modified)
            {
                parts.Add(Strings.Format(R.ExtFile_Modified, modified.ToString("g", c)));
            }

            parts.Add(Strings.Format(R.ExtFile_Words, WordCount.ToString("N0", c)));
            if (EncodingText.Length > 0)
            {
                parts.Add(EncodingText);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Reads the file; false (with <see cref="ErrorNotice"/>) when it cannot be read.</summary>
    public async Task<bool> LoadAsync()
    {
        try
        {
            Apply(await ReadAsync());
            LoadRequested?.Invoke(this, new LoadRequest(Text, CaretToEnd: false));
            Watch();
            _touched(FullPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(ex, FullPath);
            ErrorNotice = Strings.Format(R.ExtFile_OpenFailed, FileName);
            return false;
        }
    }

    public void OnTextEdited(string text)
    {
        Text = text;
        IsDirty = _document is null || !string.Equals(text, _document.Text, StringComparison.Ordinal);
        _wordCount.Schedule(async _ => SetWordCount(await Task.Run(() => NoteContentAnalyzer.Analyze(text).WordCount)));
    }

    /// <summary>Writes the editor text to the file (atomically, in its own encoding); false when it failed.</summary>
    public async Task<bool> SaveAsync()
    {
        if (_document is not { } document)
        {
            return false;
        }

        try
        {
            var text = Text;
            var saved = await Task.Run(() => document.SaveAsync(text, CancellationToken.None));
            _document = saved;
            ModifiedAt = saved.ModifiedUtc.ToLocalTime();
            IsDirty = !string.Equals(text, Text, StringComparison.Ordinal);
            ChangedOnDisk = false;
            IsDeletedOnDisk = false;
            ErrorNotice = null;
            OnPropertyChanged(nameof(StatusText));
            _touched(FullPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex, FullPath);
            ErrorNotice = Strings.Format(R.ExtFile_SaveFailed, FileName);
            return false;
        }
    }

    /// <summary>Drops the edits and shows the file as it is on disk.</summary>
    public async Task ReloadAsync()
    {
        ChangedOnDisk = false;
        try
        {
            Apply(await ReadAsync());
            LoadRequested?.Invoke(this, new LoadRequest(Text, CaretToEnd: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(ex, FullPath);
        }
    }

    /// <summary>Overwrites the version on disk with the editor's.</summary>
    public Task<bool> KeepMineAsync()
    {
        ChangedOnDisk = false;
        return SaveAsync();
    }

    /// <summary>Creates a note in today's folder with the editor's text; it is then a note like any other.</summary>
    public async Task<NotePath> CopyToNotesAsync()
    {
        var text = Text;
        return await Task.Run(() => _host.Engine.CreateAsync(DateTime.Now, tag: null, text, CancellationToken.None));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _wordCount.Dispose();
        _diskChange.Dispose();
    }

    /// <summary>The file and its word count, both off the UI thread.</summary>
    private Task<(ExternalDocument Document, int Words)> ReadAsync()
    {
        var path = FullPath;
        var token = _host.ShutdownToken;
        return Task.Run(async () =>
        {
            var document = await ExternalDocument.LoadAsync(path, token).ConfigureAwait(false);
            return (document, NoteContentAnalyzer.Analyze(document.Text).WordCount);
        });
    }

    private void Apply((ExternalDocument Document, int Words) read)
    {
        var (document, words) = read;
        _document = document;
        Text = document.Text;
        IsDirty = false;
        IsDeletedOnDisk = false;
        CreatedAt = document.CreatedUtc.ToLocalTime();
        ModifiedAt = document.ModifiedUtc.ToLocalTime();
        EncodingText = (document.HasBom ? "UTF-8 BOM" : "UTF-8") + " · " + (document.UsesCrLf ? "CRLF" : "LF");
        WordCount = words;
        OnPropertyChanged(nameof(StatusText));
    }

    private void SetWordCount(int count)
    {
        WordCount = count;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Watches the file's folder for this one name: editors often save by replacing the file.</summary>
    private void Watch()
    {
        if (_watcher is not null || DirectoryName.Length == 0)
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(DirectoryName, FileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += OnDiskEvent;
            _watcher.Created += OnDiskEvent;
            _watcher.Deleted += OnDiskEvent;
            _watcher.Renamed += OnDiskEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // No watching (e.g. a network share without notifications): edits elsewhere are not picked up.
            LogWatchFailed(ex, FullPath);
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnDiskEvent(object sender, FileSystemEventArgs e) =>
        UiThread.Post(() => _diskChange.Schedule(_ => OnDiskChangedAsync()));

    private async Task OnDiskChangedAsync()
    {
        if (_document is not { } document)
        {
            return;
        }

        var path = FullPath;
        var state = await Task.Run(() => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null);
        if (state is not { } modified)
        {
            IsDeletedOnDisk = true;
            return;
        }

        IsDeletedOnDisk = false;
        if (modified == document.ModifiedUtc)
        {
            return; // our own save
        }

        if (IsDirty)
        {
            ChangedOnDisk = true;
        }
        else
        {
            await ReloadAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening the external file {Path} failed")]
    private partial void LogOpenFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the external file {Path} failed")]
    private partial void LogSaveFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Changes of the external file {Path} are not watched")]
    private partial void LogWatchFailed(Exception ex, string path);
}
