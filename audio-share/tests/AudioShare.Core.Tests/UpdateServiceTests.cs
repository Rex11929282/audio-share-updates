using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task DownloadAndStageAsync_StagesSingleSetupForSilentInstallation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            var setup = Encoding.UTF8.GetBytes("setup");
            using var client = CreatePackageClient(setup);
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 14),
                new Uri("https://example.com/FlowCast-Setup.exe"),
                new Uri("https://example.com/FlowCast-Setup.exe.sha256"));

            var staged = await service.DownloadAndStageAsync(update);

            Assert.True(staged.UsesInstaller);
            Assert.Equal("FlowCast-Setup.exe", Path.GetFileName(staged.ExtractedDirectory));
            Assert.Equal(setup, await File.ReadAllBytesAsync(staged.ExtractedDirectory));
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

        Assert.Contains("$MaximumAttempts = 30", script);
        Assert.Contains("for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++)", script);
        Assert.Contains("replace-and-restart.log", script);
        Assert.Contains("Start-Sleep -Seconds 1", script);
        Assert.Contains("New-Item -ItemType Directory -Path $StagedApplicationDirectory -Force", script);
        Assert.DoesNotContain("Remove-Item -LiteralPath $TargetPath", script);
    }

    [Fact]
    public void ReplacementScriptSwapsTheEntirePackageDirectoryAndPreservesTheUninstaller()
    {
        var script = GetReplacementScript();

        Assert.Contains("Get-ChildItem -LiteralPath $SourceDirectory -Force", script);
        Assert.Contains("Copy-Item -LiteralPath $Item.FullName -Destination $StagedApplicationDirectory -Recurse -Force", script);
        Assert.Contains("Move-Item -LiteralPath $ApplicationDirectory -Destination $BackupApplicationDirectory", script);
        Assert.Contains("Move-Item -LiteralPath $StagedApplicationDirectory -Destination $ApplicationDirectory", script);
        Assert.Contains("Uninstall FlowCast.exe", script);
        Assert.DoesNotContain("$Files = @(", script);
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
    public async Task ReplacementScriptSwapsNestedPackageContentAndRemovesStaleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastReplacementTests", Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(root, "FlowCast");
        var package = Path.Combine(root, "package");
        Directory.CreateDirectory(Path.Combine(installed, "router-helper"));
        Directory.CreateDirectory(Path.Combine(package, "router-helper"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(installed, "AudioShare.App.exe"), "old-app");
            await File.WriteAllTextAsync(Path.Combine(package, "AudioShare.App.exe"), "new-app");
            await File.WriteAllTextAsync(Path.Combine(installed, "Uninstall FlowCast.exe"), "uninstaller");
            await File.WriteAllTextAsync(Path.Combine(installed, "stale.txt"), "stale");
            await File.WriteAllTextAsync(Path.Combine(installed, "router-helper", "audio_share_router_helper.py"), "old");
            await File.WriteAllTextAsync(Path.Combine(package, "ThirdPartyNotices.txt"), "notices");
            await File.WriteAllTextAsync(Path.Combine(package, "router-helper", "audio_share_router_helper.py"), "new");
            await File.WriteAllTextAsync(Path.Combine(package, "router-helper", "router-helper-manifest.json"), "{}");
            var scriptPath = Path.Combine(root, "replace-and-restart.ps1");
            var transactionScript = GetReplacementScript()
                .Replace("Start-Process -FilePath $TargetPath -ArgumentList '--update-failed'", "$null = $TargetPath")
                .Replace("Start-Process -FilePath $TargetPath", "$null = $TargetPath");
            await File.WriteAllTextAsync(scriptPath, transactionScript, new UTF8Encoding(false));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                    int.MaxValue.ToString(),
                    package,
                    Path.Combine(installed, "AudioShare.App.exe"),
                },
            });
            Assert.NotNull(process);
            await process.WaitForExitAsync();
            var error = await process.StandardError.ReadToEndAsync();

            Assert.True(process.ExitCode == 0, error);
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(installed, "router-helper", "audio_share_router_helper.py")));
            Assert.False(File.Exists(Path.Combine(installed, "stale.txt")));
            Assert.Equal("uninstaller", await File.ReadAllTextAsync(Path.Combine(installed, "Uninstall FlowCast.exe")));
            Assert.Empty(Directory.GetDirectories(root, ".FlowCast.flowcast-update-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecognizesUpdateFailureRestartSignal()
    {
        Assert.True(UpdateService.IsUpdateFailedRestart(["--update-failed"]));
        Assert.False(UpdateService.IsUpdateFailedRestart([]));
        Assert.Contains("上一次更新未完成", UpdateService.UpdateFailedRestartNotice);
    }

    [Theory]
    [InlineData(".FlowCast.flowcast-update-new-recovery", true)]
    [InlineData(".FlowCast.flowcast-update-backup-recovery", true)]
    [InlineData(".FlowCast.flowcast-update-new-recovery\\.FlowCast.flowcast-update-backup-recovery", true)]
    [InlineData(".unrelated-update-directory", false)]
    [InlineData("current", false)]
    [InlineData("", false)]
    public async Task DownloadAndStageAsync_CanonicalizesOnlyFlowCastTransactionDirectories(
        string executableDirectoryName,
        bool usesInstalledExecutable)
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        var installedDirectory = Path.Combine(root, "installed");
        var installedExecutablePath = Path.Combine(installedDirectory, "AudioShare.App.exe");
        var executablePath = Path.Combine(installedDirectory, executableDirectoryName, "AudioShare.App.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        try
        {
            await File.WriteAllTextAsync(installedExecutablePath, "installed");
            await File.WriteAllTextAsync(executablePath, "running");
            using var client = CreatePackageClient(CreatePackage(includeRouterHelper: true));
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            var staged = await service.DownloadAndStageAsync(update);

            Assert.Equal(usesInstalledExecutable ? installedExecutablePath : executablePath, staged.ExecutablePath);
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_UsesRegisteredInstallDirectoryForTemporaryUpdateLaunches()
    {
        var installedDirectory = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"), "installed");
        var temporaryExecutablePath = Path.Combine(
            Path.GetTempPath(),
            "FlowCast",
            "updates",
            Guid.NewGuid().ToString("N"),
            "extracted",
            "AudioShare.App.exe");
        Directory.CreateDirectory(installedDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryExecutablePath)!);
        try
        {
            var installedExecutablePath = Path.Combine(installedDirectory, "AudioShare.App.exe");
            await File.WriteAllTextAsync(installedExecutablePath, "installed");
            await File.WriteAllTextAsync(temporaryExecutablePath, "running");
            using var client = CreatePackageClient(CreatePackage(includeRouterHelper: true));
            var service = new UpdateService(client, temporaryExecutablePath, installedDirectory);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            var staged = await service.DownloadAndStageAsync(update);

            Assert.Equal(installedExecutablePath, staged.ExecutablePath);
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(installedDirectory, recursive: true);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(temporaryExecutablePath)!)!, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_UsesCanonicalExecutableNameWhenRecoveringFromMalformedTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        var installedDirectory = Path.Combine(root, "installed");
        var installedExecutablePath = Path.Combine(installedDirectory, "AudioShare.App.exe");
        var executablePath = Path.Combine(installedDirectory, ".FlowCast.flowcast-update-new-recovery", "Malformed.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        try
        {
            await File.WriteAllTextAsync(installedExecutablePath, "installed");
            await File.WriteAllTextAsync(executablePath, "running");
            using var client = CreatePackageClient(CreatePackage(includeRouterHelper: true));
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            var staged = await service.DownloadAndStageAsync(update);

            Assert.Equal(installedExecutablePath, staged.ExecutablePath);
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_RequiresAndKeepsNestedRouterHelperContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            var package = CreatePackage(includeRouterHelper: true);
            using var client = CreatePackageClient(package);
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            var staged = await service.DownloadAndStageAsync(update);

            Assert.True(File.Exists(Path.Combine(staged.ExtractedDirectory, "router-helper", "audio_share_router_helper.py")));
            Assert.True(File.Exists(Path.Combine(staged.ExtractedDirectory, "router-helper", "router-helper-manifest.json")));
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_RejectsPackageWithoutRouterHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            var package = CreatePackage(includeRouterHelper: false);
            using var client = CreatePackageClient(package);
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(update));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_ReportsAllProgressStagesForKnownLengthPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            var package = CreatePackage(includeRouterHelper: true);
            using var client = CreatePackageClient(package);
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));
            var updates = new List<UpdateProgress>();

            var staged = await service.DownloadAndStageAsync(update, new Progress<UpdateProgress>(updates.Add));

            var stageIndexes = updates
                .Select((item, index) => (item.Stage, index))
                .GroupBy(item => item.Stage)
                .ToDictionary(group => group.Key, group => group.Min(item => item.index));
            Assert.Contains(updates, item => item.Stage == UpdateStage.Downloading && item.Percentage == 100);
            Assert.True(stageIndexes[UpdateStage.Downloading] < stageIndexes[UpdateStage.Verifying]);
            Assert.True(stageIndexes[UpdateStage.Verifying] < stageIndexes[UpdateStage.Extracting]);
            Assert.True(stageIndexes[UpdateStage.Extracting] < stageIndexes[UpdateStage.ReadyToRestart]);
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_DoesNotReportVerifyingBeforeDownloadingProgressCompletes()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            using var client = CreatePackageClient(CreatePackage(includeRouterHelper: true));
            var service = new UpdateService(client, executablePath);
            var downloadingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowDownloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verifyingReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new List<string>();
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            async Task ReportProgressAsync(UpdateProgress progress)
            {
                if (progress.Stage == UpdateStage.Downloading)
                {
                    events.Add("Downloading started");
                    downloadingEntered.TrySetResult();
                    await allowDownloading.Task;
                    events.Add("Downloading completed");
                    return;
                }

                events.Add(progress.Stage.ToString());
                if (progress.Stage == UpdateStage.Verifying)
                {
                    verifyingReported.TrySetResult();
                }
            }

            var stagingTask = service.DownloadAndStageAsync(update, ReportProgressAsync);
            await downloadingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var observedEvent = await Task.WhenAny(verifyingReported.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(verifyingReported.Task, observedEvent);
            Assert.False(stagingTask.IsCompleted);

            allowDownloading.SetResult();
            var staged = await stagingTask;
            Assert.True(events.IndexOf("Downloading completed") < events.IndexOf(nameof(UpdateStage.Verifying)));
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_WaitsForExtractingProgressBeforeExtractingPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            using var client = CreatePackageClient(CreatePackage(includeRouterHelper: true));
            var service = new UpdateService(client, executablePath);
            var extractingReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowExtraction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));

            Task ReportProgressAsync(UpdateProgress progress)
            {
                if (progress.Stage != UpdateStage.Extracting)
                {
                    return Task.CompletedTask;
                }

                extractingReported.TrySetResult();
                return allowExtraction.Task;
            }

            var stagingTask = service.DownloadAndStageAsync(update, ReportProgressAsync);
            await extractingReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(stagingTask.IsCompleted);

            allowExtraction.SetResult();
            var staged = await stagingTask;
            Assert.True(File.Exists(Path.Combine(staged.ExtractedDirectory, "AudioShare.App.exe")));
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndStageAsync_StagesPackageWithUnknownContentLength()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlowCastTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executablePath = Path.Combine(root, "installed", "AudioShare.App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            await File.WriteAllTextAsync(executablePath, "old");
            var package = CreatePackage(includeRouterHelper: true);
            using var client = CreatePackageClient(package, contentLengthKnown: false);
            var service = new UpdateService(client, executablePath);
            var update = new ReleaseUpdate(
                new Version(2, 0, 1),
                new Uri("https://example.com/AudioShare-win-x64.zip"),
                new Uri("https://example.com/AudioShare-win-x64.zip.sha256"));
            var updates = new List<UpdateProgress>();

            var staged = await service.DownloadAndStageAsync(update, new Progress<UpdateProgress>(updates.Add));

            Assert.NotEmpty(updates.Where(item => item.Stage == UpdateStage.Downloading));
            Assert.All(
                updates.Where(item => item.Stage == UpdateStage.Downloading),
                item => Assert.Null(item.Percentage));
            Assert.True(File.Exists(Path.Combine(staged.ExtractedDirectory, "AudioShare.App.exe")));
            Directory.Delete(staged.UpdateDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UpdateProgress_UsesIndeterminateProgressWhenByteCountIsUnavailable()
    {
        Assert.Null(new UpdateProgress(UpdateStage.Downloading, "正在下载更新", null).Percentage);
    }

    [Fact]
    public void ReplacementScript_AutomaticallyStartsTheUpdatedExecutable()
    {
        Assert.Contains("Start-Process -FilePath $TargetPath", GetReplacementScript());
    }

    [Fact]
    public void InstallerReplacementScript_UsesSilentSetupAndValidatesInstalledVersion()
    {
        var script = GetInstallerReplacementScript();

        Assert.Contains("Start-Process -FilePath $InstallerPath -ArgumentList '/S'", script);
        Assert.Contains("VersionInfo.FileVersion", script);
        Assert.Contains("StartsWith(\"$ExpectedVersion.\")", script);
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

    private static string GetInstallerReplacementScript()
    {
        var field = typeof(UpdateService).GetField("InstallerReplacementScript", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<string>(field.GetValue(null));
    }

    private static byte[] CreatePackage(bool includeRouterHelper)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "AudioShare.App.exe", "app");
            WriteEntry(archive, "ThirdPartyNotices.txt", "notices");
            if (includeRouterHelper)
            {
                WriteEntry(archive, "router-helper/audio_share_router_helper.py", "helper");
                WriteEntry(archive, "router-helper/router-helper-manifest.json", "{}");
            }
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static HttpClient CreatePackageClient(byte[] package, bool contentLengthKnown = true)
    {
        var checksum = Encoding.UTF8.GetBytes(
            $"{Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant()}  AudioShare-win-x64.zip");
        return new HttpClient(new PackageResponseHandler(package, checksum, contentLengthKnown));
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

    private sealed class PackageResponseHandler(byte[] package, byte[] checksum, bool contentLengthKnown) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal) == true
                ? checksum
                : package;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = contentLengthKnown || request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal) == true
                    ? new ByteArrayContent(content)
                    : new UnknownLengthContent(content),
            });
        }
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
