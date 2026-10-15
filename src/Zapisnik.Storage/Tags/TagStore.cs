using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zapisnik.Core.Settings;
using Zapisnik.Core.Tags;
using Zapisnik.Core.Text;

namespace Zapisnik.Storage.Tags;

/// <summary>
/// Tag colours and order from <c>&lt;root&gt;/.zapisnik/tags.json</c> (SPEC §3.5). Tags without an entry
/// get a deterministic palette colour, so the file only stores user overrides.
/// </summary>
public sealed partial class TagStore
{
    private readonly string _path;
    private readonly string _relativePath;
    private readonly SelfWriteRegistry _selfWrites;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private TagsDocument _document = new();
    private Dictionary<string, TagDefinition> _byKey = new(StringComparer.Ordinal);

    public TagStore(string root, SelfWriteRegistry selfWrites, ILogger<TagStore> logger)
    {
        _relativePath = Watching.NoteWatcher.TagsRelativePath;
        _path = Path.Combine(root, ".zapisnik", "tags.json");
        _selfWrites = selfWrites;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public string FilePath => _path;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        TagsDocument doc;
        try
        {
            if (!File.Exists(_path))
            {
                doc = new TagsDocument();
            }
            else
            {
                await using var stream = File.OpenRead(_path);
                doc = await JsonSerializer.DeserializeAsync(stream, ZapisnikJsonContext.Default.TagsDocument, cancellationToken).ConfigureAwait(false)
                      ?? new TagsDocument();
            }
        }
        catch (JsonException ex)
        {
            // A broken file must not break the app; keep palette colours and do not overwrite it silently.
            LogInvalidFile(ex, _path);
            doc = new TagsDocument();
        }

        lock (_gate)
        {
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

    public Task SetColorAsync(string tag, TagColor color, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.Color = color.ToHex(), cancellationToken);

    public Task SetShowInCalendarLabelsAsync(string tag, bool show, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.ShowInCalendarLabels = show, cancellationToken);

    public Task SetOrderAsync(string tag, int order, CancellationToken cancellationToken) =>
        UpdateAsync(tag, d => d.Order = order, cancellationToken);

    private async Task UpdateAsync(string tag, Action<TagDefinition> update, CancellationToken cancellationToken)
    {
        if (!TagName.TryNormalize(tag, out var normalized))
        {
            throw new ArgumentException($"Invalid tag '{tag}'.", nameof(tag));
        }

        string json;
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
            json = JsonSerializer.Serialize(_document, ZapisnikJsonContext.Default.TagsDocument);
        }

        _selfWrites.RecordWrite(_relativePath, ContentHash.Compute(json));
        await AtomicFile.WriteAllTextAsync(_path, json, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid tags file {Path}")]
    private partial void LogInvalidFile(Exception ex, string path);
}
