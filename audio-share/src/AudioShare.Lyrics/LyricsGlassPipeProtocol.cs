using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AudioShare.Lyrics;

internal static class LyricsGlassPipeProtocol
{
    internal const int CurrentVersion = 1;
    internal const int MaximumFrameBytes = 65_536;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> RendererEventTypes = new(StringComparer.Ordinal)
    {
        "hello",
        "ready",
        "settings-committed",
        "position-changed",
        "open-options",
        "close-request",
        "fault"
    };

    internal static async Task WriteAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(json);

        var body = StrictUtf8.GetBytes(json);
        if (body.Length is <= 0 or > MaximumFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(json));
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<string?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        var headerBytes = await ReadUntilEndAsync(stream, header, cancellationToken);
        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != header.Length)
        {
            throw new InvalidDataException("Pipe frame ended during its header.");
        }

        var bodyLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (bodyLength is <= 0 or > MaximumFrameBytes)
        {
            throw new InvalidDataException("Pipe frame length is invalid.");
        }

        var body = new byte[bodyLength];
        if (await ReadUntilEndAsync(stream, body, cancellationToken) != body.Length)
        {
            throw new InvalidDataException("Pipe frame ended during its body.");
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Pipe frame is not valid UTF-8.", exception);
        }

        ValidateRendererEnvelope(json);
        return json;
    }

    private static async Task<int> ReadUntilEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void ValidateRendererEnvelope(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var envelope = document.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var parsedVersion) ||
                parsedVersion != CurrentVersion ||
                !envelope.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                !RendererEventTypes.Contains(type.GetString()!))
            {
                throw new InvalidDataException("Pipe frame envelope is invalid.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Pipe frame is not valid JSON.", exception);
        }
    }
}
