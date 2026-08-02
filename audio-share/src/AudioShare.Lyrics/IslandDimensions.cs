using AudioShare.Lyrics.Contracts;

namespace AudioShare.Lyrics;

public readonly record struct IslandSize(double Width, double Height);

public static class IslandDimensions
{
    public static IslandSize For(IslandMode mode) => mode switch
    {
        IslandMode.Idle => new IslandSize(180, 44),
        IslandMode.Resolving => new IslandSize(280, 52),
        IslandMode.Playing or IslandMode.Paused => new IslandSize(680, 92),
        IslandMode.NoLyrics or IslandMode.Unavailable => new IslandSize(320, 52),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
