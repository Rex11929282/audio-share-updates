using System.Reflection;
using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public void DoesNotOfferMatchingReleaseForInstalledAssemblyVersion()
    {
        var installedVersion = GetCurrentVersion(typeof(UpdateServiceTests).Assembly);

        Assert.Equal(new Version(1, 0, 1, 0), installedVersion);
        Assert.Null(ReleaseUpdateParser.TryParseNewerRelease(ReleaseJson("1.0.1"), installedVersion));
    }

    [Fact]
    public void ReplacementScriptUsesBoundedRetriesWithoutDeletingTarget()
    {
        var script = GetReplacementScript();

        Assert.Contains("$MaximumAttempts = 5", script);
        Assert.Contains("for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++)", script);
        Assert.Contains("Copy-Item -LiteralPath $SourcePath -Destination $StagedPath -Force", script);
        Assert.DoesNotContain("Remove-Item -LiteralPath $TargetPath", script);
    }

    [Fact]
    public void ReplacementScriptRelaunchesTargetAfterReplacementAttempts()
    {
        var script = GetReplacementScript();
        var retryLoopIndex = script.IndexOf("for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++)", StringComparison.Ordinal);
        var restartIndex = script.IndexOf("Start-Process -FilePath $TargetPath", StringComparison.Ordinal);

        Assert.True(retryLoopIndex >= 0);
        Assert.True(restartIndex > retryLoopIndex);
    }

    private static Version GetCurrentVersion(Assembly assembly)
    {
        var method = typeof(UpdateService).GetMethod("GetCurrentVersion", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<Version>(method.Invoke(null, [assembly]));
    }

    private static string GetReplacementScript()
    {
        var field = typeof(UpdateService).GetField("ReplacementScript", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<string>(field.GetValue(null));
    }

    private static string ReleaseJson(string version) => $$"""
        {
          "tag_name": "{{version}}",
          "assets": [
            { "name": "AudioShare-win-x64.zip", "browser_download_url": "https://example.com/AudioShare-win-x64.zip" },
            { "name": "AudioShare-win-x64.zip.sha256", "browser_download_url": "https://example.com/AudioShare-win-x64.zip.sha256" }
          ]
        }
        """;
}
