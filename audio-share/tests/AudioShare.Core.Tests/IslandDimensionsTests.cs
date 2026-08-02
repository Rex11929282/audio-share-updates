using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class IslandDimensionsTests
{
    [Theory]
    [InlineData(IslandMode.Idle, 180, 44)]
    [InlineData(IslandMode.Resolving, 280, 52)]
    [InlineData(IslandMode.Playing, 680, 92)]
    [InlineData(IslandMode.Paused, 680, 92)]
    [InlineData(IslandMode.NoLyrics, 320, 52)]
    [InlineData(IslandMode.Unavailable, 320, 52)]
    public void For_ReturnsTheApprovedDynamicIslandSize(
        IslandMode mode,
        double width,
        double height) =>
        Assert.Equal(new IslandSize(width, height), IslandDimensions.For(mode));
}
