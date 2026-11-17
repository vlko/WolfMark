using System.Buffers.Binary;
using System.Text;

namespace WolfMark.Sync.Share;

/// <summary>An image sent with a share; <see cref="Extension"/> without the dot, one of <see cref="ShareMessage.ImageExtensions"/>.</summary>
public sealed record ShareAttachment(string Extension, ReadOnlyMemory<byte> Data);

/// <summary>
/// What the phone sends: a finished Markdown note and its images. The text refers to image <c>n</c> as
/// <c>wolfmark-attachment:n</c>; the receiver stores the image and puts the real link there.
/// </summary>
/// <param name="Id">Identifies the share across retries: the same id never becomes two notes.</param>
/// <param name="SentAt">The phone's clock when this attempt was sent – old frames are refused (replay).</param>
/// <param name="CreatedLocal">When the user shared it, on the phone's wall clock: the note's day and time.</param>
public sealed record ShareMessage(Guid Id, DateTimeOffset SentAt, DateTime CreatedLocal, string Markdown, IReadOnlyList<ShareAttachment> Attachments)
{
    public const byte Version = 1;
    public const int MaxAttachments = 20;
    public const int MaxMarkdownBytes = 4 * 1024 * 1024;
    public const string AttachmentPrefix = "wolfmark-attachment:";

    /// <summary>Bytes before the text: version, id, two times, text length.</summary>
    private const int HeaderLength = 1 + 16 + 8 + 8 + 4;

    /// <summary>The image types the apps show (as <c>MarkdownImageLink</c>) – nothing else is written to <c>assets</c>.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = ["png", "jpg", "jpeg", "gif", "bmp", "webp"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// <c>version (1) | id (16, RFC 4122 order) | sent, Unix ms (8) | created (8) | text length (4) | UTF-8 text |
    /// image count (1) | { extension length (1) | ASCII extension | length (4) | bytes }*</c>, numbers big-endian.
    /// "Created" is the phone's wall clock written as if it were UTC, in Unix ms: no time zone travels, the note
    /// gets the day and time the user saw on the phone.
    /// </summary>
    public byte[] Encode()
    {
        var text = StrictUtf8.GetBytes(Markdown);
        var size = HeaderLength + text.Length + 1;
        foreach (var a in Attachments)
        {
            size += 1 + a.Extension.Length + 4 + a.Data.Length;
        }

        var buffer = new byte[size];
        var span = buffer.AsSpan();
        span[0] = Version;
        Id.TryWriteBytes(span.Slice(1, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(span[17..], SentAt.ToUnixTimeMilliseconds());
        var created = new DateTimeOffset(DateTime.SpecifyKind(CreatedLocal, DateTimeKind.Unspecified), TimeSpan.Zero);
        BinaryPrimitives.WriteInt64BigEndian(span[25..], created.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32BigEndian(span[33..], text.Length);
        text.CopyTo(span[HeaderLength..]);
        var at = HeaderLength + text.Length;
        span[at++] = checked((byte)Attachments.Count);
        foreach (var a in Attachments)
        {
            span[at++] = checked((byte)a.Extension.Length);
            at += Encoding.ASCII.GetBytes(a.Extension, span[at..]);
            BinaryPrimitives.WriteInt32BigEndian(span[at..], a.Data.Length);
            at += 4;
            a.Data.Span.CopyTo(span[at..]);
            at += a.Data.Length;
        }

        return buffer;
    }

    /// <summary>False for anything malformed: wrong version, lengths past the end, bad UTF-8, an unknown image type, trailing bytes.</summary>
    public static bool TryDecode(ReadOnlyMemory<byte> data, out ShareMessage message)
    {
        message = null!;
        var span = data.Span;
        if (span.Length < HeaderLength + 1 || span[0] != Version)
        {
            return false;
        }

        var id = new Guid(span.Slice(1, 16), bigEndian: true);
        var sentMs = BinaryPrimitives.ReadInt64BigEndian(span[17..]);
        var createdMs = BinaryPrimitives.ReadInt64BigEndian(span[25..]);
        var textLength = BinaryPrimitives.ReadInt32BigEndian(span[33..]);
        if (textLength is < 0 or > MaxMarkdownBytes || textLength > span.Length - HeaderLength - 1)
        {
            return false;
        }

        DateTimeOffset sent;
        DateTime createdLocal;
        string markdown;
        try
        {
            sent = DateTimeOffset.FromUnixTimeMilliseconds(sentMs);
            createdLocal = DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(createdMs).UtcDateTime, DateTimeKind.Unspecified);
            markdown = StrictUtf8.GetString(span.Slice(HeaderLength, textLength));
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            // Out-of-range times and invalid UTF-8 (DecoderFallbackException is an ArgumentException).
            return false;
        }

        var at = HeaderLength + textLength;
        int count = span[at++];
        if (count > MaxAttachments)
        {
            return false;
        }

        var attachments = new List<ShareAttachment>(count);
        for (var i = 0; i < count; i++)
        {
            if (at >= span.Length)
            {
                return false;
            }

            int extensionLength = span[at++];
            if (extensionLength > span.Length - at - 4)
            {
                return false;
            }

            var extension = Encoding.ASCII.GetString(span.Slice(at, extensionLength));
            at += extensionLength;
            var length = BinaryPrimitives.ReadInt32BigEndian(span[at..]);
            at += 4;
            if (!ImageExtensions.Contains(extension, StringComparer.Ordinal) || length < 0 || length > span.Length - at)
            {
                return false;
            }

            attachments.Add(new ShareAttachment(extension, data.Slice(at, length)));
            at += length;
        }

        if (at != span.Length)
        {
            return false;
        }

        message = new ShareMessage(id, sent, createdLocal, markdown, attachments);
        return true;
    }
}

public enum ShareStatus : byte
{
    /// <summary>The note was created.</summary>
    Created = 0,

    /// <summary>This share already became a note (a retry) – nothing was written.</summary>
    Duplicate = 1,

    /// <summary>Refused: the frame is too old or from the future. The phone delivers it another way.</summary>
    Refused = 2,

    /// <summary>The message could not be read or the note could not be written.</summary>
    Failed = 3,
}

/// <summary>The answer: <c>version (1) | status (1) | path length (2) | UTF-8 path</c> of the note, relative to the notes.</summary>
public sealed record ShareResponse(ShareStatus Status, string NotePath)
{
    public byte[] Encode()
    {
        var path = Encoding.UTF8.GetBytes(NotePath);
        var buffer = new byte[4 + path.Length];
        buffer[0] = ShareMessage.Version;
        buffer[1] = (byte)Status;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), checked((ushort)path.Length));
        path.CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out ShareResponse response)
    {
        response = null!;
        if (data.Length < 4 || data[0] != ShareMessage.Version || data[1] > (byte)ShareStatus.Failed
            || BinaryPrimitives.ReadUInt16BigEndian(data[2..]) != data.Length - 4)
        {
            return false;
        }

        response = new ShareResponse((ShareStatus)data[1], Encoding.UTF8.GetString(data[4..]));
        return true;
    }
}
