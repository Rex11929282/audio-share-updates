using System.Text.Json;

namespace AudioShare.Core;

public sealed record ReleaseUpdate(Version Version, Uri AssetUrl, Uri Sha256Url);

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
            var setupChecksumUrl = FindAssetUrl(assets, SetupChecksumAssetName);
            if (setupUrl is not null && setupChecksumUrl is not null)
            {
                return new ReleaseUpdate(version, setupUrl, setupChecksumUrl);
            }

            var packageUrl = FindAssetUrl(assets, PackageAssetName);
            var checksumUrl = FindAssetUrl(assets, ChecksumAssetName);

            return packageUrl is not null && checksumUrl is not null
                ? new ReleaseUpdate(version, packageUrl, checksumUrl)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
}
