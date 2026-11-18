using System.Globalization;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Tags;
using WolfMark.Storage;

namespace WolfMark.Sync.Share;

/// <summary>
/// Turns a received <see cref="ShareMessage"/> into a note with the tag <see cref="TagName.Share"/>: images go to
/// the day's <c>assets</c>, the note is created like any other (<see cref="DirectoryEngine.CreateAsync(DateTime, string?, string, CancellationToken)"/>),
/// so the index and the window pick it up through the usual events.
/// </summary>
public sealed partial class ShareReceiver(DirectoryEngine engine, TimeProvider time, ILogger<ShareReceiver> logger)
{
    /// <summary>How far the phone's "sent" time may be from this computer's clock; older frames may be replays.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>Shares remembered for retries. In memory only: after a restart the clock check is what is left.</summary>
    private const int RememberedShares = 256;

    private static readonly DateTime EarliestNote = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, string> _received = [];
    private readonly Queue<Guid> _order = new();

    /// <summary>A share became a note (not raised for a retry of one that already did). Background thread.</summary>
    public event EventHandler<WolfMark.Core.Notes.NotePath>? Received;

    public async Task<ShareResponse> ReceiveAsync(ShareMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = time.GetUtcNow();
        var nowLocal = time.GetLocalNow().DateTime;
        if ((now - message.SentAt).Duration() > MaxClockSkew
            || message.CreatedLocal < EarliestNote
            || message.CreatedLocal > nowLocal.AddDays(2))
        {
            LogRefused(message.Id, message.SentAt);
            return new ShareResponse(ShareStatus.Refused, string.Empty);
        }

        // One share at a time: a retry that arrives while the first attempt is still being written must find it.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_received.TryGetValue(message.Id, out var existing))
            {
                return new ShareResponse(ShareStatus.Duplicate, existing);
            }

            var markdown = message.Markdown;
            var day = DateOnly.FromDateTime(message.CreatedLocal);
            // Highest number first: "…:1" must not eat the start of "…:10".
            for (var i = message.Attachments.Count - 1; i >= 0; i--)
            {
                var attachment = message.Attachments[i];
                var link = await engine.SaveAssetAsync(day, message.CreatedLocal, attachment.Extension, attachment.Data, cancellationToken).ConfigureAwait(false);
                var placeholder = ShareMessage.AttachmentPrefix + i.ToString(CultureInfo.InvariantCulture);
                markdown = markdown.Contains(placeholder, StringComparison.Ordinal)
                    ? markdown.Replace(placeholder, link, StringComparison.Ordinal)
                    : markdown.TrimEnd('\r', '\n') + "\n\n![](" + link + ")\n";
            }

            var path = await engine.CreateAsync(message.CreatedLocal, TagName.Share, markdown, cancellationToken).ConfigureAwait(false);
            Remember(message.Id, path.RelativePath);
            LogReceived(path.RelativePath, message.Attachments.Count);
            Received?.Invoke(this, path);
            return new ShareResponse(ShareStatus.Created, path.RelativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(ex, message.Id);
            return new ShareResponse(ShareStatus.Failed, string.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Remember(Guid id, string path)
    {
        _received[id] = path;
        _order.Enqueue(id);
        while (_order.Count > RememberedShares)
        {
            _received.Remove(_order.Dequeue());
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Share: note {Path} received with {Images} images")]
    private partial void LogReceived(string path, int images);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share: {Id} refused, sent at {SentAt} – too far from this computer's clock, or an impossible note time")]
    private partial void LogRefused(Guid id, DateTimeOffset sentAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Share: {Id} could not be written")]
    private partial void LogFailed(Exception ex, Guid id);
}
