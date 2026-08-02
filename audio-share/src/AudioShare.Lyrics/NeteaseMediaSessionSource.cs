using System.Runtime.CompilerServices;
using Windows.Media.Control;

namespace AudioShare.Lyrics;

public sealed record MediaPlaybackSnapshot(
    string Title,
    IReadOnlyList<string> Artists,
    long DurationMilliseconds,
    long PositionMilliseconds,
    DateTimeOffset CapturedAtUtc,
    bool IsPlaying)
{
    public long PositionAt(DateTimeOffset now)
    {
        var elapsed = IsPlaying ? (long)(now - CapturedAtUtc).TotalMilliseconds : 0;
        return Math.Clamp(PositionMilliseconds + elapsed, 0, Math.Max(0, DurationMilliseconds));
    }
}

public interface INeteaseMediaSessionSource
{
    IAsyncEnumerable<MediaPlaybackSnapshot?> WatchAsync(CancellationToken cancellationToken);
}

public sealed class NeteaseMediaSessionSource : INeteaseMediaSessionSource
{
    private static readonly char[] ArtistSeparators = ['/', '／', '、', ';', '；', ','];

    public async IAsyncEnumerable<MediaPlaybackSnapshot?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        MediaPlaybackSnapshot? previous = null;
        var emitted = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var current = await ReadCurrentAsync(manager);
            if (!emitted || !Equivalent(previous, current))
            {
                previous = current;
                emitted = true;
                yield return current;
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken))
            {
                yield break;
            }
        }
    }

    public static bool IsNeteaseSource(string? sourceAppUserModelId) =>
        !string.IsNullOrWhiteSpace(sourceAppUserModelId) &&
        (sourceAppUserModelId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
         sourceAppUserModelId.Contains("netease", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ParseArtists(string? artist) =>
        string.IsNullOrWhiteSpace(artist)
            ? []
            : artist.Split(ArtistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => value.Length > 0)
                .ToArray();

    private static async Task<MediaPlaybackSnapshot?> ReadCurrentAsync(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var session = manager.GetSessions().FirstOrDefault(candidate =>
            IsNeteaseSource(candidate.SourceAppUserModelId));
        if (session is null)
        {
            return null;
        }

        var media = await session.TryGetMediaPropertiesAsync();
        if (string.IsNullOrWhiteSpace(media.Title))
        {
            return null;
        }

        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();
        return new MediaPlaybackSnapshot(
            media.Title.Trim(),
            ParseArtists(media.Artist),
            Math.Max(0, (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds),
            Math.Max(0, (long)(timeline.Position - timeline.StartTime).TotalMilliseconds),
            DateTimeOffset.UtcNow,
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    private static bool Equivalent(MediaPlaybackSnapshot? left, MediaPlaybackSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Title == right.Title &&
               left.Artists.SequenceEqual(right.Artists, StringComparer.Ordinal) &&
               left.DurationMilliseconds == right.DurationMilliseconds &&
               left.PositionMilliseconds == right.PositionMilliseconds &&
               left.IsPlaying == right.IsPlaying;
    }
}
