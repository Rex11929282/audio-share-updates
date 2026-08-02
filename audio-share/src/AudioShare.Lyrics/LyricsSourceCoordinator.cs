using AudioShare.Lyrics.Contracts;

namespace AudioShare.Lyrics;

public sealed record LocalLyricsPlayback(
    string TrackId,
    MediaPlaybackSnapshot Media,
    NeteaseLyricsStatus LyricsStatus,
    IReadOnlyList<LyricLine> Lines);

public sealed record RemoteLyricsPlayback(
    string SessionId,
    long Sequence,
    IslandMode Mode,
    LyricLine? LyricLine,
    DateTimeOffset ReceivedAtUtc);

public sealed class LyricsSourceCoordinator(TimeSpan remoteLifetime)
{
    private LocalLyricsPlayback? local;
    private RemoteLyricsPlayback? remote;

    public void SetLocal(LocalLyricsPlayback? playback)
    {
        local = playback;
    }

    public void SetRemote(RemoteLyricsPlayback playback)
    {
        if (string.IsNullOrWhiteSpace(playback.SessionId) || playback.Sequence <= 0)
        {
            return;
        }

        if (remote is not null && playback.ReceivedAtUtc - remote.ReceivedAtUtc > remoteLifetime)
        {
            remote = null;
        }

        if (remote is not null &&
            (!string.Equals(remote.SessionId, playback.SessionId, StringComparison.Ordinal) ||
             playback.Sequence <= remote.Sequence))
        {
            return;
        }

        remote = playback;
    }

    public void ClearRemote()
    {
        remote = null;
    }

    public IslandSnapshot SnapshotAt(DateTimeOffset now)
    {
        if (remote is not null)
        {
            if (now - remote.ReceivedAtUtc <= remoteLifetime)
            {
                return CreateRemoteSnapshot(remote);
            }

            remote = null;
        }

        return CreateLocalSnapshot(now);
    }

    private IslandSnapshot CreateLocalSnapshot(DateTimeOffset now)
    {
        if (local is null)
        {
            return new IslandSnapshot(IslandMode.Idle, "等待播放", null, LyricSource.None);
        }

        if (local.LyricsStatus == NeteaseLyricsStatus.NoLyrics)
        {
            return new IslandSnapshot(IslandMode.NoLyrics, "這首歌沒有歌詞", null, LyricSource.LocalNetEase);
        }

        if (local.LyricsStatus == NeteaseLyricsStatus.Unavailable)
        {
            return new IslandSnapshot(IslandMode.Unavailable, "暫時無法取得歌詞", null, LyricSource.LocalNetEase);
        }

        var position = local.Media.PositionAt(now);
        var line = local.Lines
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Text) &&
                candidate.StartTimeMilliseconds <= position &&
                (candidate.EndTimeMilliseconds is null || position < candidate.EndTimeMilliseconds))
            .OrderBy(candidate => candidate.StartTimeMilliseconds)
            .LastOrDefault();
        if (line is null)
        {
            return new IslandSnapshot(IslandMode.Resolving, "正在取得歌詞", null, LyricSource.LocalNetEase);
        }

        var normalizedLine = line with { Text = line.Text.Trim() };
        return new IslandSnapshot(
            local.Media.IsPlaying ? IslandMode.Playing : IslandMode.Paused,
            normalizedLine.Text,
            normalizedLine,
            LyricSource.LocalNetEase);
    }

    private static IslandSnapshot CreateRemoteSnapshot(RemoteLyricsPlayback playback)
    {
        var line = string.IsNullOrWhiteSpace(playback.LyricLine?.Text)
            ? null
            : playback.LyricLine with { Text = playback.LyricLine.Text.Trim() };
        if (playback.Mode is IslandMode.Playing or IslandMode.Paused && line is not null)
        {
            return new IslandSnapshot(
                playback.Mode,
                line.Text,
                line,
                LyricSource.RemoteRadmin);
        }

        return playback.Mode switch
        {
            IslandMode.NoLyrics => new IslandSnapshot(
                IslandMode.NoLyrics,
                "這首歌沒有歌詞",
                null,
                LyricSource.RemoteRadmin),
            IslandMode.Unavailable => new IslandSnapshot(
                IslandMode.Unavailable,
                "暫時無法取得歌詞",
                null,
                LyricSource.RemoteRadmin),
            IslandMode.Idle => new IslandSnapshot(
                IslandMode.Idle,
                "等待播放",
                null,
                LyricSource.RemoteRadmin),
            _ => new IslandSnapshot(
                IslandMode.Resolving,
                "正在取得歌詞",
                null,
                LyricSource.RemoteRadmin)
        };
    }
}
