using System.IO;
using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class LyricsCacheTests
{
    [Fact]
    public async Task FileCache_RoundTripsRawLyricsAndReturnsNullForAMissingTrack()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var cache = new FileLyricsCache(root);

            await cache.WriteAsync("496869422", "[00:01.00]真實歌詞", CancellationToken.None);

            Assert.Equal(
                "[00:01.00]真實歌詞",
                await cache.ReadAsync("496869422", CancellationToken.None));
            Assert.Null(await cache.ReadAsync("404", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("not-a-track")]
    [InlineData("")]
    public async Task FileCache_RejectsNonNumericTrackIds(string trackId)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var cache = new FileLyricsCache(root);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                cache.WriteAsync(trackId, "[00:01.00]不可寫入", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FlowCastLyricsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
