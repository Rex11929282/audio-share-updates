using System.Net;
using System.Net.Http;
using System.Text;
using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class NeteaseLyricsClientTests
{
    [Fact]
    public async Task GetAsync_ReturnsTimedLyricsAndCachesTheRawLrc()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"nolyric\":false,\"lrc\":{\"lyric\":\"[00:01.00]真實歌詞\"}}");
        var cache = new MemoryLyricsCache();
        var client = new NeteaseLyricsClient(new HttpClient(handler), cache);

        var result = await client.GetAsync("496869422", CancellationToken.None);

        Assert.Equal(NeteaseLyricsStatus.Available, result.Status);
        Assert.Equal("真實歌詞", Assert.Single(result.Lines).Text);
        Assert.Equal("[00:01.00]真實歌詞", await cache.ReadAsync("496869422", CancellationToken.None));
        Assert.Contains("id=496869422", handler.LastRequestUri?.Query);
        Assert.Equal("https://music.163.com/", handler.LastReferer?.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_ReturnsNoLyricsOnlyForAnExplicitNoLyricResponse()
    {
        var client = CreateClient(
            HttpStatusCode.OK,
            "{\"nolyric\":true,\"lrc\":{\"lyric\":\"\"}}",
            new MemoryLyricsCache());

        var result = await client.GetAsync("496869422", CancellationToken.None);

        Assert.Equal(NeteaseLyricsStatus.NoLyrics, result.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task GetAsync_UsesCachedLyricsWhenTheNetworkFails()
    {
        var cache = new MemoryLyricsCache();
        await cache.WriteAsync("496869422", "[00:02.00]快取歌詞", CancellationToken.None);
        var client = CreateClient(HttpStatusCode.InternalServerError, "失敗", cache);

        var result = await client.GetAsync("496869422", CancellationToken.None);

        Assert.Equal(NeteaseLyricsStatus.Available, result.Status);
        Assert.Equal("快取歌詞", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public async Task GetAsync_ReturnsUnavailableWhenNetworkAndCacheHaveNoTimedLyrics()
    {
        var client = CreateClient(
            HttpStatusCode.OK,
            "{\"nolyric\":false,\"lrc\":{\"lyric\":\"沒有時間戳\"}}",
            new MemoryLyricsCache());

        var result = await client.GetAsync("496869422", CancellationToken.None);

        Assert.Equal(NeteaseLyricsStatus.Unavailable, result.Status);
        Assert.Empty(result.Lines);
    }

    private static NeteaseLyricsClient CreateClient(
        HttpStatusCode statusCode,
        string content,
        ILyricsCache cache) =>
        new(new HttpClient(new StubHttpMessageHandler(statusCode, content)), cache);

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public Uri? LastReferer { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastReferer = request.Headers.Referrer;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MemoryLyricsCache : ILyricsCache
    {
        private readonly Dictionary<string, string> entries = [];

        public Task<string?> ReadAsync(string trackId, CancellationToken cancellationToken) =>
            Task.FromResult(entries.GetValueOrDefault(trackId));

        public Task WriteAsync(string trackId, string lrc, CancellationToken cancellationToken)
        {
            entries[trackId] = lrc;
            return Task.CompletedTask;
        }
    }
}
