using System.IO;
using System.Reflection;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ReleaseUpdateParserTests
{
    [Fact]
    public void ParsesSingleSetupAssetWithGitHubDigest()
    {
        var update = ReleaseUpdateParser.TryParseNewerRelease(
            """
            {
              "tag_name": "v2.0.16",
              "assets": [
                {
                  "name": "FlowCast-Setup.exe",
                  "browser_download_url": "https://example.com/FlowCast-Setup.exe",
                  "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
              ]
            }
            """,
            new Version(2, 0, 15));

        Assert.NotNull(update);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", update.ExpectedSha256);
        Assert.Null(update.Sha256Url);
    }

    [Fact]
    public void ParsesNewerReleaseWithSingleSetupAsset()
    {
        var update = ReleaseUpdateParser.TryParseNewerRelease(
            """
            {
              "tag_name": "v2.0.14",
              "assets": [
                { "name": "FlowCast-Setup.exe", "browser_download_url": "https://example.com/FlowCast-Setup.exe" },
                { "name": "FlowCast-Setup.exe.sha256", "browser_download_url": "https://example.com/FlowCast-Setup.exe.sha256" }
              ]
            }
            """,
            new Version(2, 0, 13));

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 0, 14), update.Version);
        Assert.Equal("FlowCast-Setup.exe", Path.GetFileName(update.AssetUrl.LocalPath));
    }

    [Fact]
    public void ParsesReleaseNotesForTheUpdateDialog()
    {
        var update = ReleaseUpdateParser.TryParseNewerRelease(
            """
            {
              "tag_name": "v2.0.14",
              "body": "- Improved routing\n- Fixed the timer\n\nMore detail",
              "assets": [
                { "name": "FlowCast-Setup.exe", "browser_download_url": "https://example.com/FlowCast-Setup.exe" },
                { "name": "FlowCast-Setup.exe.sha256", "browser_download_url": "https://example.com/FlowCast-Setup.exe.sha256" }
              ]
            }
            """,
            new Version(2, 0, 13));

        Assert.NotNull(update);
        Assert.Equal(["Improved routing", "Fixed the timer"], update.Notes);
    }

    [Fact]
    public void Readme_DescribesCurrentFlowCastRoutingAndUpdatePolicy()
    {
        var readmePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "README.md"));
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("sends only the applications you select", readme);
        Assert.Contains("FlowCast-Setup.exe", readme);
        Assert.DoesNotContain("Voicemod", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_StatesThatFlowCastChangesSelectedApplicationRoutes()
    {
        var readmePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "README.md"));
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("before changing any routes", readme, StringComparison.Ordinal);
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
