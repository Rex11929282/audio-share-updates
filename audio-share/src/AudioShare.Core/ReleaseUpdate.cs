using System.Text.Json;

namespace AudioShare.Core;

public sealed record ReleaseUpdate(
    Version Version,
    Uri AssetUrl,
    Uri? Sha256Url,
    string? ExpectedSha256 = null,
    IReadOnlyList<string>? Notes = null);

public static class ReleaseUpdateParser
{
    public const string SetupAssetName = "FlowCast-Setup.exe";
    public const string SetupChecksumAssetName = "FlowCast-Setup.exe.sha256";
    public const string PackageAssetName = "AudioShare-win-x64.zip";
    public const string ChecksumAssetName = "AudioShare-win-x64.zip.sha256";

    public static ReleaseUpdate? TryParseNewerRelease(string releaseJson, Version currentVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagName) ||
                !Version.TryParse(tagName.GetString()?.TrimStart('v', 'V'), out var version) ||
                version <= currentVersion ||
                !root.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var setupUrl = FindAssetUrl(assets, SetupAssetName);
            var setupDigest = FindSha256Digest(assets, SetupAssetName);
            var notes = ParseNotes(root);
            if (setupUrl is not null && setupDigest is not null)
            {
                return new ReleaseUpdate(version, setupUrl, null, setupDigest, notes);
            }

            var setupChecksumUrl = FindAssetUrl(assets, SetupChecksumAssetName);
            if (setupUrl is not null && setupChecksumUrl is not null)
            {
                return new ReleaseUpdate(version, setupUrl, setupChecksumUrl, Notes: notes);
            }

            var packageUrl = FindAssetUrl(assets, PackageAssetName);
            var checksumUrl = FindAssetUrl(assets, ChecksumAssetName);

            return packageUrl is not null && checksumUrl is not null
                ? new ReleaseUpdate(version, packageUrl, checksumUrl, Notes: notes)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParseNotes(JsonElement root)
    {
        if (!root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var lines = body.GetString()!
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var bulletNotes = lines
            .Where(line => line.StartsWith('-') || line.StartsWith('*'))
            .Select(line => line.TrimStart('-', '*', ' '))
            .ToArray();

        return (bulletNotes.Length > 0 ? bulletNotes : lines)
            .Take(3)
            .ToArray();
    }

    private static Uri? FindAssetUrl(JsonElement assets, string assetName)
    {
        Uri? selectedUrl = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.GetString() != assetName)
            {
                continue;
            }

            if (selectedUrl is not null ||
                !asset.TryGetProperty("browser_download_url", out var downloadUrl) ||
                !Uri.TryCreate(downloadUrl.GetString(), UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            selectedUrl = url;
        }

        return selectedUrl;
    }

    private static string? FindSha256Digest(JsonElement assets, string assetName)
    {
        string? selectedDigest = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.GetString() != assetName)
            {
                continue;
            }

            if (selectedDigest is not null ||
                !asset.TryGetProperty("digest", out var digest) ||
                !TryNormalizeSha256(digest.GetString(), out var normalizedDigest))
            {
                return null;
            }

            selectedDigest = normalizedDigest;
        }

        return selectedDigest;
    }

    private static bool TryNormalizeSha256(string? digest, out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hash = digest[prefix.Length..];
        try
        {
            normalizedDigest = Convert.ToHexString(Convert.FromHexString(hash)).ToLowerInvariant();
            return normalizedDigest.Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
