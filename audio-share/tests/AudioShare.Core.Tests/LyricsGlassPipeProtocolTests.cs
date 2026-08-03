using System.IO;
using System.Text;

namespace AudioShare.Core.Tests;

public sealed class LyricsGlassPipeProtocolTests
{
    [Fact]
    public async Task WriteAsync_UsesABigEndianHeaderAndRoundTripsAValidatedFrame()
    {
        const string json = "{\"version\":1,\"type\":\"ready\"}";
        await using var stream = new MemoryStream();

        await global::AudioShare.Lyrics.LyricsGlassPipeProtocol.WriteAsync(stream, json, CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, (byte)Encoding.UTF8.GetByteCount(json) }, bytes[..4]);
        stream.Position = 0;
        Assert.Equal(json, await global::AudioShare.Lyrics.LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullForCleanEofBeforeAHeader()
    {
        await using var stream = new MemoryStream();

        Assert.Null(await global::AudioShare.Lyrics.LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(InvalidFrames))]
    public async Task ReadAsync_RejectsInvalidFrames(byte[] frame)
    {
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            global::AudioShare.Lyrics.LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptyAndOversizedPayloads()
    {
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            global::AudioShare.Lyrics.LyricsGlassPipeProtocol.WriteAsync(stream, string.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            global::AudioShare.Lyrics.LyricsGlassPipeProtocol.WriteAsync(stream, new string('x', 65_537), CancellationToken.None));
    }

    public static IEnumerable<object[]> InvalidFrames()
    {
        yield return new object[] { new byte[] { 0, 0, 0 } }; // partial header
        yield return new object[] { Frame(5, Encoding.UTF8.GetBytes("{}")) }; // partial body
        yield return new object[] { new byte[] { 0, 0, 0, 0 } }; // zero length
        yield return new object[] { new byte[] { 0, 1, 0, 1 } }; // over maximum
        yield return new object[] { Frame(2, new byte[] { 0xc3, 0x28 }) }; // invalid UTF-8
        yield return new object[] { Frame("{ broken") };
        yield return new object[] { Frame("{\"version\":2,\"type\":\"ready\"}") };
        yield return new object[] { Frame("{\"version\":1,\"type\":\"unexpected\"}") };
        yield return new object[] { Frame("{\"type\":\"ready\"}") };
        yield return new object[] { Frame("{\"version\":1}") };
        yield return new object[] { Frame("{\"version\":\"1\",\"type\":\"ready\"}") };
        yield return new object[] { Frame("{\"version\":1,\"type\":1}") };
    }

    private static byte[] Frame(string json) => Frame(Encoding.UTF8.GetByteCount(json), Encoding.UTF8.GetBytes(json));

    private static byte[] Frame(int declaredLength, byte[] body)
    {
        var frame = new byte[4 + body.Length];
        frame[0] = (byte)(declaredLength >> 24);
        frame[1] = (byte)(declaredLength >> 16);
        frame[2] = (byte)(declaredLength >> 8);
        frame[3] = (byte)declaredLength;
        body.CopyTo(frame, 4);
        return frame;
    }
}
