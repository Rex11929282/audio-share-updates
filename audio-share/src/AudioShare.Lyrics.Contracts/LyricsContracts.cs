namespace AudioShare.Lyrics.Contracts;

public enum ConnectionState
{
    Searching,
    Unavailable,
    Connected,
    Disconnected
}

public enum IslandMode
{
    Idle,
    Resolving,
    Playing,
    Paused,
    NoLyrics,
    Unavailable
}

public enum LyricSource
{
    None,
    LocalNetEase,
    RemoteRadmin
}

public sealed record LyricLine(
    string Text,
    long StartTimeMilliseconds,
    long? EndTimeMilliseconds);

public sealed record IslandSnapshot(
    IslandMode Mode,
    string DisplayText,
    LyricLine? LyricLine,
    LyricSource Source);

public interface ILyricsOverlaySink
{
    void Show(IslandSnapshot snapshot);
}
