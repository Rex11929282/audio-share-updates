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

    [Fact]
    public async Task ReadAsync_AssemblesAFrameAcrossArbitraryShortReads()
    {
        const string json = "{\"version\":1,\"type\":\"settings-committed\"}";
        await using var stream = new FragmentedReadStream(Frame(json), 1, 2, 1, 3, 1);

        var result = await global::AudioShare.Lyrics.LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(json, result);
    }

    [Fact]
    public async Task ReadAsync_AcceptsOpenOptionsEventEnvelope()
    {
        const string json = "{\"version\":1,\"type\":\"open-options\"}";
        await using var stream = new MemoryStream(Frame(json));

        Assert.Equal(json, await global::AudioShare.Lyrics.LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None));
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

    private sealed class FragmentedReadStream(byte[] contents, params int[] chunkSizes) : Stream
    {
        private int offset;
        private int chunkIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => contents.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset == contents.Length)
            {
                return ValueTask.FromResult(0);
            }

            var count = Math.Min(
                Math.Min(buffer.Length, chunkSizes[chunkIndex++ % chunkSizes.Length]),
                contents.Length - offset);
            contents.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
