using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioShare.Lyrics;

public interface ILyricsCache
{
    Task<string?> ReadAsync(string trackId, CancellationToken cancellationToken);

    Task WriteAsync(string trackId, string lrc, CancellationToken cancellationToken);
}

public sealed partial class FileLyricsCache : ILyricsCache
{
    private readonly string rootDirectory;

    public FileLyricsCache(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(this.rootDirectory);
    }

    public async Task<string?> ReadAsync(string trackId, CancellationToken cancellationToken)
    {
        var path = GetTrackPath(trackId);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            : null;
    }

    public Task WriteAsync(string trackId, string lrc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lrc);
        return File.WriteAllTextAsync(GetTrackPath(trackId), lrc, Encoding.UTF8, cancellationToken);
    }

    private string GetTrackPath(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId) || !TrackIdRegex().IsMatch(trackId))
        {
            throw new ArgumentException("NetEase track IDs must contain only digits.", nameof(trackId));
        }

        return Path.Combine(rootDirectory, $"{trackId}.lrc");
    }

    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex TrackIdRegex();
}
