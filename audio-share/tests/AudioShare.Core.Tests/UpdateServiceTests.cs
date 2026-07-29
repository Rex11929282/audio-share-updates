using System.Net;
using System.Net.Http;
using System.Reflection;
using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task TreatsGitHubLatestReleaseNotFoundAsNoUpdate()
    {
        using var client = new HttpClient(new StaticResponseHandler(HttpStatusCode.NotFound));
        var service = new UpdateService(client);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

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
    public void ReplacementScriptReplacesTheExecutableAndThirdPartyNotices()
    {
        var script = GetReplacementScript();

        Assert.Contains("$Files = @(\"AudioShare.App.exe\", \"ThirdPartyNotices.txt\")", script);
        Assert.Contains("[System.IO.File]::Replace($StagedPath, $FileTargetPath, $BackupPath, $true)", script);
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

    [Fact]
    public void ReplacementScriptSignalsFailureWhenAllReplacementAttemptsFail()
    {
        var script = GetReplacementScript();

        Assert.Contains("$ReplacementSucceeded = $false", script);
        Assert.Contains("$ReplacementSucceeded = $true", script);
        Assert.Contains("Start-Process -FilePath $TargetPath -ArgumentList '--update-failed'", script);
    }

    [Fact]
    public void RecognizesUpdateFailureRestartSignal()
    {
        Assert.True(UpdateService.IsUpdateFailedRestart(["--update-failed"]));
        Assert.False(UpdateService.IsUpdateFailedRestart([]));
        Assert.Contains("上一個更新未完成", UpdateService.UpdateFailedRestartNotice);
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

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
