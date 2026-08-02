using System.Net.Http;
using System.Text.Json;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Lyrics;

public enum NeteaseLyricsStatus
{
    Available,
    NoLyrics,
    Unavailable
}

public sealed record NeteaseLyricsResult(
    NeteaseLyricsStatus Status,
    IReadOnlyList<LyricLine> Lines);

public interface INeteaseLyricsProvider
{
    Task<NeteaseLyricsResult> GetAsync(string trackId, CancellationToken cancellationToken);
}

public sealed class NeteaseLyricsClient(HttpClient httpClient, ILyricsCache cache) : INeteaseLyricsProvider
{
    public async Task<NeteaseLyricsResult> GetAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://music.163.com/api/song/lyric?id={Uri.EscapeDataString(trackId)}&lv=-1&kv=-1&tv=-1");
            request.Headers.Referrer = new Uri("https://music.163.com/");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("nolyric", out var noLyric) &&
                    noLyric.ValueKind == JsonValueKind.True)
                {
                    return new NeteaseLyricsResult(NeteaseLyricsStatus.NoLyrics, []);
                }

                if (TryReadRawLrc(document.RootElement, out var rawLrc))
                {
                    var lines = LrcParser.Parse(rawLrc);
                    if (lines.Count > 0)
                    {
                        await cache.WriteAsync(trackId, rawLrc, cancellationToken);
                        return new NeteaseLyricsResult(NeteaseLyricsStatus.Available, lines);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
        }
        catch (JsonException)
        {
        }

        var cachedLrc = await cache.ReadAsync(trackId, cancellationToken);
        var cachedLines = LrcParser.Parse(cachedLrc ?? string.Empty);
        return cachedLines.Count > 0
            ? new NeteaseLyricsResult(NeteaseLyricsStatus.Available, cachedLines)
            : new NeteaseLyricsResult(NeteaseLyricsStatus.Unavailable, []);
    }

    private static bool TryReadRawLrc(JsonElement root, out string rawLrc)
    {
        rawLrc = string.Empty;
        if (!root.TryGetProperty("lrc", out var lrc) ||
            !lrc.TryGetProperty("lyric", out var lyric) ||
            lyric.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        rawLrc = lyric.GetString() ?? string.Empty;
        return rawLrc.Length > 0;
    }
}
