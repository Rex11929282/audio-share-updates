namespace AudioShare.Lyrics.Contracts;

public enum ConnectionState
{
    Searching,
    Unavailable,
    Connected,
    Disconnected
}

public sealed record LyricLine(
    string Text,
    long StartTimeMilliseconds,
    long? EndTimeMilliseconds);

public interface ILyricsOverlaySink
{
    void SetConnectionState(ConnectionState state);

    void ShowLyricLine(LyricLine? line);
}
