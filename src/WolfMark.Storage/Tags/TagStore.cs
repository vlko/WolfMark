using System.Text.Json;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Core.Text;

namespace WolfMark.Storage.Tags;

/// <summary>
/// Tag colours and order from <c>&lt;root&gt;/.wolfmark/tags.json</c> (SPEC §3.5). Tags without an entry
/// get a deterministic palette colour, so the file only stores user overrides.
/// </summary>
public sealed partial class TagStore
{
    private readonly string _path;
    private readonly string _relativePath;
    private readonly SelfWriteRegistry _selfWrites;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>The file on disk was not what is in memory (unreadable or invalid): keep a copy before the first write.</summary>
    private bool _keepCopyBeforeWrite;
    private TagsDocument _document = new();
    private Dictionary<string, TagDefinition> _byKey = new(StringComparer.Ordinal);

    public TagStore(string root, SelfWriteRegistry selfWrites, ILogger<TagStore> logger)
    {
        _relativePath = Watching.NoteWatcher.TagsRelativePath;
        _path = Path.Combine(root, ".wolfmark", "tags.json");
        _selfWrites = selfWrites;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public string FilePath => _path;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        TagsDocument doc;
        var unread = false;
        try
        {
            if (!File.Exists(_path))
            {
                doc = new TagsDocument();
            }
            else
            {
                await using var stream = File.OpenRead(_path);
                doc = await JsonSerializer.DeserializeAsync(stream, WolfMarkJsonContext.Default.TagsDocument, cancellationToken).ConfigureAwait(false)
                      ?? new TagsDocument();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken or locked file must not break the app: palette colours for now, and the first change keeps
            // a copy of the file instead of silently replacing the user's colours and order.
            LogInvalidFile(ex, _path);
            doc = new TagsDocument();
            unread = true;
        }

        lock (_gate)
        {
            _keepCopyBeforeWrite = unread;
            _document = doc;
            _byKey = doc.Tags
                .Where(t => TagName.IsValid(t.Name))
                .GroupBy(t => TagName.ToKey(t.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public TagColor GetColor(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return TagPalette.Untagged;
        }

        lock (_gate)
        {
            if (_byKey.TryGetValue(TagName.ToKey(tag), out var def) && TagColor.TryParse(def.Color, out var color))
            {
                return color;
            }
        }

        return TagPalette.ForTag(tag);
    }

    public bool ShowsInCalendarLabels(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return false;
        }

        lock (_gate)
        {
            return _byKey.TryGetValue(TagName.ToKey(tag), out var def) && def.ShowInCalendarLabels;
        }
    }

    public IReadOnlyList<string> LabelTags
    {
        get
        {
            lock (_gate)
            {
                return [.. _byKey.Values.Where(d => d.ShowInCalendarLabels).Select(d => d.Name)];
            }
        }
    }

    /// <summary>User-defined order (lower first); tags without an entry sort after, by name.</summary>
    public int GetOrder(string tag)
    {
        lock (_gate)
        {
            return _byKey.TryGetValue(TagName.ToKey(tag), out var def) ? def.Order : int.MaxValue;
        }
    }

    /// <summary>Tags written in tags.json – also ones without notes (added in Settings, or whose notes are gone).</summary>
    public IReadOnlyList<string> DefinedTags
    {
        get
        {
            lock (_gate)
            {
                return [.. _byKey.Values.Select(d => d.Name)];
            }
        }
    }

    /// <summary>A new tag without notes yet (Settings → Tags): written to tags.json so the tag pickers offer it.</summary>
    public Task AddAsync(string tag, CancellationToken cancellationToken) => UpdateAsync(tag, _ => { }, cancellationToken);

    /// <summary>A removed tag (Settings → Tags): its colour, labels and order are forgotten.</summary>
    public async Task RemoveAsync(string tag, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = TagName.ToKey(tag);
            if (!_byKey.Remove(key, out var def))
            {
                return;
            }

            _document.Tags.Remove(def);
        }

        await WriteAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task SetColorAsync(string tag, TagColor color, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.Color = color.ToHex(), cancellationToken);

    public Task SetShowInCalendarLabelsAsync(string tag, bool show, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.ShowInCalendarLabels = show, cancellationToken);

    public Task SetOrderAsync(string tag, int order, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.Order = order, cancellationToken);

    /// <summary>
    /// After a tag rename (M8-05): the new tag takes over the old one's colour, labels and order – a rename keeps the
    /// look, even though a palette colour comes from the name. A tag that already exists (a merge) keeps its own.
    /// </summary>
    public async Task RenameAsync(string oldTag, string newTag, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var oldKey = TagName.ToKey(oldTag);
            var newKey = TagName.ToKey(newTag);
            _byKey.TryGetValue(oldKey, out var old);
            if (oldKey == newKey)
            {
                if (old is null)
                {
                    return; // only the letter case changed and nothing is stored
                }

                old.Name = newTag;
            }
            else if (_byKey.ContainsKey(newKey))
            {
                if (old is null)
                {
                    return;
                }

                _document.Tags.Remove(old); // merge: the target keeps its colour
                _byKey.Remove(oldKey);
            }
            else
            {
                if (old is null)
                {
                    // The old palette colour, written down – otherwise the new name would pick another one.
                    old = new TagDefinition { Order = _document.Tags.Count };
                    old.Color = TagPalette.ForTag(oldTag).ToHex();
                    _document.Tags.Add(old);
                }
                else
                {
                    old.Color ??= TagPalette.ForTag(oldTag).ToHex();
                    _byKey.Remove(oldKey);
                }

                old.Name = newTag;
                _byKey[newKey] = old;
            }

        }

        await WriteAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task UpdateAsync(string tag, Action<TagDefinition> update, CancellationToken cancellationToken)
    {
        if (!TagName.TryNormalize(tag, out var normalized))
        {
            throw new ArgumentException($"Invalid tag '{tag}'.", nameof(tag));
        }

        lock (_gate)
        {
            var key = TagName.ToKey(normalized);
            if (!_byKey.TryGetValue(key, out var def))
            {
                def = new TagDefinition { Name = normalized, Order = _document.Tags.Count };
                _document.Tags.Add(def);
                _byKey[key] = def;
            }

            update(def);
        }

        await WriteAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the current state. One write at a time and the state is taken inside the turn, so two quick changes
    /// can neither land in the wrong order nor share the temp file.
    /// </summary>
    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string json;
            bool keepCopy;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_document, WolfMarkJsonContext.Default.TagsDocument);
                keepCopy = _keepCopyBeforeWrite;
            }

            if (keepCopy)
            {
                if (BrokenFile.KeepCopy(_path) is { } copy)
                {
                    LogKeptCopy(_path, copy);
                }

                lock (_gate)
                {
                    _keepCopyBeforeWrite = false;
                }
            }

            _selfWrites.RecordWrite(_relativePath, ContentHash.Compute(json));
            await AtomicFile.WriteAllTextAsync(_path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tags file {Path} could not be read – the old file was kept as {Copy}")]
    private partial void LogKeptCopy(string path, string copy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid tags file {Path}")]
    private partial void LogInvalidFile(Exception ex, string path);
}
