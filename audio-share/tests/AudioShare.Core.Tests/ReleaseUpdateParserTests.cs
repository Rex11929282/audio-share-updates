using System.IO;
using System.Reflection;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ReleaseUpdateParserTests
{
    [Fact]
    public void DocumentsSelectedSourceEnginePhaseOneBoundary()
    {
        var readmePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "README.md"));
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("does not install a virtual microphone driver", readme);
    }

    [Fact]
    public void PublishScriptCopiesEveryBundledNoticeIntoTheArchiveSource()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "publish-release.ps1"));
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("ThirdPartyNotices.txt", script);
        Assert.Contains("DotNetRuntimeLicense.txt", script);
        Assert.Contains("DotNetRuntimeThirdPartyNotices.txt", script);
        Assert.Contains("Copy-Item -LiteralPath $thirdPartyNoticesPath -Destination $publishDirectory", script);
        Assert.Contains("Copy-Item -LiteralPath $dotNetRuntimeLicensePath -Destination $publishDirectory", script);
        Assert.Contains("Copy-Item -LiteralPath $dotNetRuntimeThirdPartyNoticesPath -Destination $publishDirectory", script);
    }

    [Fact]
    public void ParsesNewerReleaseWithRequiredAssets()
    {
        var update = Parse("""
            {
              "tag_name": "v1.0.1",
              "assets": [
                { "name": "AudioShare-win-x64.zip", "browser_download_url": "https://example.com/AudioShare-win-x64.zip" },
                { "name": "AudioShare-win-x64.zip.sha256", "browser_download_url": "https://example.com/AudioShare-win-x64.zip.sha256" }
              ]
            }
            """);

        Assert.NotNull(update);
        Assert.Equal("1.0.1", GetProperty(update, "Version")?.ToString());
        Assert.Equal("https://example.com/AudioShare-win-x64.zip", GetProperty(update, "AssetUrl")?.ToString());
        Assert.Equal("https://example.com/AudioShare-win-x64.zip.sha256", GetProperty(update, "Sha256Url")?.ToString());
    }

    [Fact]
    public void RejectsReleaseWithNonHttpsAssetUrl()
    {
        var update = Parse("""
            {
              "tag_name": "v1.0.1",
              "assets": [
                { "name": "AudioShare-win-x64.zip", "browser_download_url": "http://example.com/AudioShare-win-x64.zip" },
                { "name": "AudioShare-win-x64.zip.sha256", "browser_download_url": "https://example.com/AudioShare-win-x64.zip.sha256" }
              ]
            }
            """);

        Assert.Null(update);
    }

    [Fact]
    public void RejectsReleaseWithoutChecksumAsset()
    {
        var update = Parse("""
            {
              "tag_name": "v1.0.1",
              "assets": [
                { "name": "AudioShare-win-x64.zip", "browser_download_url": "https://example.com/AudioShare-win-x64.zip" }
              ]
            }
            """);

        Assert.Null(update);
    }

    [Fact]
    public void RejectsReleaseThatIsNotNewerThanCurrentVersion()
    {
        var update = Parse("""
            {
              "tag_name": "1.0.0",
              "assets": [
                { "name": "AudioShare-win-x64.zip", "browser_download_url": "https://example.com/AudioShare-win-x64.zip" },
                { "name": "AudioShare-win-x64.zip.sha256", "browser_download_url": "https://example.com/AudioShare-win-x64.zip.sha256" }
              ]
            }
            """);

        Assert.Null(update);
    }

    private static object? Parse(string releaseJson)
    {
        var parserType = typeof(AudioSession).Assembly.GetType("AudioShare.Core.ReleaseUpdateParser");
        Assert.NotNull(parserType);

        var parseMethod = parserType.GetMethod("TryParseNewerRelease", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(parseMethod);
        return parseMethod.Invoke(null, [releaseJson, new Version(1, 0, 0)]);
    }

    private static object? GetProperty(object value, string propertyName) =>
        value.GetType().GetProperty(propertyName)?.GetValue(value);
}
