using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class NeteaseTrackResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesAnExactNormalizedLocalMatch()
    {
        var search = new FakeSearchClient([]);
        var resolver = new NeteaseTrackResolver(
            new FakePlayingListReader([
                new NeteaseTrackCandidate("496869422", "打上花火", ["Daoko", "米津玄師"])
            ]),
            search);

        var result = await resolver.ResolveAsync("打 上、花火!", ["DAOKO"], CancellationToken.None);

        Assert.Equal("496869422", result);
        Assert.Equal(0, search.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToSearchWhenTheLocalListDoesNotMatch()
    {
        var resolver = new NeteaseTrackResolver(
            new FakePlayingListReader([new NeteaseTrackCandidate("1", "別首歌", ["別人"])]),
            new FakeSearchClient([new NeteaseTrackCandidate("496869422", "打上花火", ["Daoko"])]));

        Assert.Equal(
            "496869422",
            await resolver.ResolveAsync("打上花火", ["Daoko"], CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_RejectsAnArtistMismatch()
    {
        var resolver = new NeteaseTrackResolver(
            new FakePlayingListReader([]),
            new FakeSearchClient([new NeteaseTrackCandidate("496869422", "打上花火", ["其他歌手"])]));

        Assert.Null(await resolver.ResolveAsync("打上花火", ["Daoko"], CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_RejectsAmbiguousLocalMatchesWithoutSearching()
    {
        var search = new FakeSearchClient([new NeteaseTrackCandidate("3", "同名歌", ["同歌手"])]);
        var resolver = new NeteaseTrackResolver(
            new FakePlayingListReader([
                new NeteaseTrackCandidate("1", "同名歌", ["同歌手"]),
                new NeteaseTrackCandidate("2", "同名歌", ["同歌手"])
            ]),
            search);

        Assert.Null(await resolver.ResolveAsync("同名歌", ["同歌手"], CancellationToken.None));
        Assert.Equal(0, search.CallCount);
    }

    private sealed class FakePlayingListReader(IReadOnlyList<NeteaseTrackCandidate> candidates)
        : INeteasePlayingListReader
    {
        public Task<IReadOnlyList<NeteaseTrackCandidate>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(candidates);
    }

    private sealed class FakeSearchClient(IReadOnlyList<NeteaseTrackCandidate> candidates)
        : INeteaseSearchClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<NeteaseTrackCandidate>> SearchAsync(
            string title,
            IReadOnlyList<string> artists,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(candidates);
        }
    }
}
