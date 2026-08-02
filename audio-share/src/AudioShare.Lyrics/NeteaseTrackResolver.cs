using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AudioShare.Lyrics;

public sealed record NeteaseTrackCandidate(string Id, string Title, IReadOnlyList<string> Artists);

public interface INeteasePlayingListReader
{
    Task<IReadOnlyList<NeteaseTrackCandidate>> ReadAsync(CancellationToken cancellationToken);
}

public interface INeteaseSearchClient
{
    Task<IReadOnlyList<NeteaseTrackCandidate>> SearchAsync(
        string title,
        IReadOnlyList<string> artists,
        CancellationToken cancellationToken);
}

public interface INeteaseTrackResolver
{
    Task<string?> ResolveAsync(
        string title,
        IReadOnlyList<string> artists,
        CancellationToken cancellationToken);
}

public sealed class NeteaseTrackResolver(
    INeteasePlayingListReader playingListReader,
    INeteaseSearchClient searchClient) : INeteaseTrackResolver
{
    public async Task<string?> ResolveAsync(
        string title,
        IReadOnlyList<string> artists,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || artists.Count == 0)
        {
            return null;
        }

        var localMatches = FindMatches(
            await playingListReader.ReadAsync(cancellationToken),
            title,
            artists);
        if (localMatches.Count == 1)
        {
            return localMatches[0].Id;
        }

        if (localMatches.Count > 1)
        {
            return null;
        }

        var searchMatches = FindMatches(
            await searchClient.SearchAsync(title, artists, cancellationToken),
            title,
            artists);
        return searchMatches.Count == 1 ? searchMatches[0].Id : null;
    }

    private static IReadOnlyList<NeteaseTrackCandidate> FindMatches(
        IReadOnlyList<NeteaseTrackCandidate> candidates,
        string title,
        IReadOnlyList<string> artists)
    {
        var normalizedTitle = Normalize(title);
        var normalizedArtists = artists
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return candidates
            .Where(candidate => Normalize(candidate.Title) == normalizedTitle)
            .Where(candidate => candidate.Artists.Select(Normalize).Any(normalizedArtists.Contains))
            .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

public sealed class NeteasePlayingListReader(string path) : INeteasePlayingListReader
{
    public static NeteasePlayingListReader FromLocalApplicationData()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new NeteasePlayingListReader(Path.Combine(
            localApplicationData,
            "NetEase",
            "CloudMusic",
            "webdata",
            "file",
            "playingList"));
    }

    public async Task<IReadOnlyList<NeteaseTrackCandidate>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var candidates = new List<NeteaseTrackCandidate>();
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("track", out var track) ||
                    !TryReadId(track, out var id) ||
                    !track.TryGetProperty("name", out var nameElement) ||
                    string.IsNullOrWhiteSpace(nameElement.GetString()))
                {
                    continue;
                }

                candidates.Add(new NeteaseTrackCandidate(
                    id,
                    nameElement.GetString()!,
                    ReadArtists(track)));
            }

            return candidates;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static bool TryReadId(JsonElement track, out string id)
    {
        id = string.Empty;
        if (!track.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString() ?? string.Empty,
            JsonValueKind.Number => idElement.GetRawText(),
            _ => string.Empty
        };
        return id.Length > 0;
    }

    internal static IReadOnlyList<string> ReadArtists(JsonElement track)
    {
        if (!track.TryGetProperty("artists", out var artistsElement) ||
            artistsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return artistsElement.EnumerateArray()
            .Select(artist => artist.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }
}

public sealed class NeteaseSearchClient(HttpClient httpClient) : INeteaseSearchClient
{
    public async Task<IReadOnlyList<NeteaseTrackCandidate>> SearchAsync(
        string title,
        IReadOnlyList<string> artists,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(string.Join(' ', new[] { title }.Concat(artists)));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://music.163.com/api/search/get/web?s={query}&type=1&limit=10&offset=0");
        request.Headers.Referrer = new Uri("https://music.163.com/");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<NeteaseTrackCandidate>();
        foreach (var song in songs.EnumerateArray())
        {
            if (!TryReadId(song, out var id) ||
                !song.TryGetProperty("name", out var nameElement) ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                continue;
            }

            candidates.Add(new NeteaseTrackCandidate(
                id,
                nameElement.GetString()!,
                NeteasePlayingListReader.ReadArtists(song)));
        }

        return candidates;
    }

    private static bool TryReadId(JsonElement song, out string id)
    {
        id = string.Empty;
        if (!song.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString() ?? string.Empty,
            JsonValueKind.Number => idElement.GetRawText(),
            _ => string.Empty
        };
        return id.Length > 0;
    }
}
