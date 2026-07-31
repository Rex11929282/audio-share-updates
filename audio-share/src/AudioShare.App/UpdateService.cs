using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AudioShare.Core;

namespace AudioShare.App;

public sealed record StagedUpdate(string UpdateDirectory, string ExtractedDirectory, string ExecutablePath);

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Rex11929282/audio-share-updates/releases/latest";
    private static readonly Version CurrentVersion = GetCurrentVersion(typeof(UpdateService).Assembly);
    private readonly HttpClient? client;
    private readonly string? executablePath;

    public const string UpdateFailureSignal = "--update-failed";
    public const string UpdateFailedRestartNotice = "上一次更新未完成，已保留原程序并重新启动。";

    public UpdateService()
    {
    }

    public UpdateService(HttpClient client, string? executablePath = null)
    {
        this.client = client;
        this.executablePath = executablePath;
    }

    public static bool IsUpdateFailedRestart(IEnumerable<string> arguments) =>
        arguments.Any(argument => string.Equals(argument, UpdateFailureSignal, StringComparison.Ordinal));

    public async Task<ReleaseUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var ownedClient = client is null ? CreateClient() : null;
        using var response = await (ownedClient ?? client!).GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var releaseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReleaseUpdateParser.TryParseNewerRelease(releaseJson, CurrentVersion);
    }

    public async Task<StagedUpdate> DownloadAndStageAsync(ReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        var executablePath = this.executablePath ?? Environment.ProcessPath ??
            throw new InvalidOperationException("找不到当前程序文件。");
        var applicationDirectory = Path.GetDirectoryName(executablePath) ?? throw new InvalidOperationException("找不到程序文件夹。");
        EnsureApplicationDirectoryIsWritable(applicationDirectory);
        var applicationParent = Path.GetDirectoryName(applicationDirectory) ??
            throw new InvalidOperationException("找不到程序文件夹的上级目录。");
        EnsureApplicationDirectoryIsWritable(applicationParent);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "FlowCast", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        try
        {
            var packagePath = Path.Combine(updateDirectory, ReleaseUpdateParser.PackageAssetName);
            var checksumPath = Path.Combine(updateDirectory, ReleaseUpdateParser.ChecksumAssetName);

            using (var ownedClient = client is null ? CreateClient() : null)
            {
                var httpClient = ownedClient ?? client!;
                await File.WriteAllBytesAsync(packagePath, await httpClient.GetByteArrayAsync(update.AssetUrl, cancellationToken), cancellationToken);
                await File.WriteAllBytesAsync(checksumPath, await httpClient.GetByteArrayAsync(update.Sha256Url, cancellationToken), cancellationToken);
            }

            if (!HasMatchingChecksum(packagePath, await File.ReadAllTextAsync(checksumPath, cancellationToken)))
            {
                throw new InvalidDataException("更新文件的 SHA-256 验证失败。");
            }

            var extractedDirectory = Path.Combine(updateDirectory, "extracted");
            ZipFile.ExtractToDirectory(packagePath, extractedDirectory);

            var requiredFiles = new[]
            {
                "AudioShare.App.exe",
                "ThirdPartyNotices.txt",
                Path.Combine("router-helper", "audio_share_router_helper.py"),
                Path.Combine("router-helper", "router-helper-manifest.json"),
            };
            if (requiredFiles.Any(fileName => !File.Exists(Path.Combine(extractedDirectory, fileName))))
            {
                throw new InvalidDataException("更新压缩包未包含预期的程序、授权说明或路由组件。");
            }

            return new StagedUpdate(updateDirectory, extractedDirectory, executablePath);
        }
        catch
        {
            try
            {
                Directory.Delete(updateDirectory, recursive: true);
            }
            catch
            {
                // Cleanup must not hide the original download or validation failure.
            }

            throw;
        }
    }

    public void BeginStagedReplacementAndRestart(StagedUpdate update)
    {
        var scriptPath = Path.Combine(update.UpdateDirectory, "replace-and-restart.ps1");
        File.WriteAllText(scriptPath, ReplacementScript, new UTF8Encoding(false));

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                scriptPath,
                Process.GetCurrentProcess().Id.ToString(),
                update.ExtractedDirectory,
                update.ExecutablePath,
            },
        });

        if (process is null)
        {
            throw new InvalidOperationException("无法启动更新程序。");
        }
    }

    public async Task DownloadVerifyAndRestartAsync(ReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        var stagedUpdate = await DownloadAndStageAsync(update, cancellationToken);
        BeginStagedReplacementAndRestart(stagedUpdate);
    }

    private static readonly string ReplacementScript = """
        param(
            [int]$ProcessId,
            [string]$SourceDirectory,
            [string]$TargetPath
        )

        Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
        $ApplicationDirectory = Split-Path -Parent $TargetPath
        $ApplicationParent = Split-Path -Parent $ApplicationDirectory
        $ApplicationName = Split-Path -Leaf $ApplicationDirectory
        $TransactionId = [Guid]::NewGuid().ToString("N")
        $StagedApplicationDirectory = Join-Path $ApplicationParent ".$ApplicationName.flowcast-update-new-$TransactionId"
        $BackupApplicationDirectory = Join-Path $ApplicationParent ".$ApplicationName.flowcast-update-backup-$TransactionId"
        $MaximumAttempts = 5
        $ReplacementSucceeded = $false

        for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
            try {
                Remove-Item -LiteralPath $StagedApplicationDirectory -Recurse -Force -ErrorAction SilentlyContinue
                New-Item -ItemType Directory -Path $StagedApplicationDirectory -Force | Out-Null
                $PackageItems = @(Get-ChildItem -LiteralPath $SourceDirectory -Force)
                if ($PackageItems.Count -eq 0) {
                    throw "The staged update package is empty."
                }

                foreach ($Item in $PackageItems) {
                    Copy-Item -LiteralPath $Item.FullName -Destination $StagedApplicationDirectory -Recurse -Force
                }

                $UninstallerPath = Join-Path $ApplicationDirectory "Uninstall FlowCast.exe"
                $StagedUninstallerPath = Join-Path $StagedApplicationDirectory "Uninstall FlowCast.exe"
                if ((Test-Path -LiteralPath $UninstallerPath -PathType Leaf) -and
                    -not (Test-Path -LiteralPath $StagedUninstallerPath)) {
                    Copy-Item -LiteralPath $UninstallerPath -Destination $StagedUninstallerPath -Force
                }

                Move-Item -LiteralPath $ApplicationDirectory -Destination $BackupApplicationDirectory
                Move-Item -LiteralPath $StagedApplicationDirectory -Destination $ApplicationDirectory
                $ReplacementSucceeded = $true
                Remove-Item -LiteralPath $BackupApplicationDirectory -Recurse -Force -ErrorAction SilentlyContinue
                break
            }
            catch {
                if (-not (Test-Path -LiteralPath $ApplicationDirectory) -and
                    (Test-Path -LiteralPath $BackupApplicationDirectory)) {
                    Move-Item -LiteralPath $BackupApplicationDirectory -Destination $ApplicationDirectory
                }

                Remove-Item -LiteralPath $StagedApplicationDirectory -Recurse -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 500
            }
        }

        if ($ReplacementSucceeded) {
            Start-Process -FilePath $TargetPath
        }
        else {
            Start-Process -FilePath $TargetPath -ArgumentList '--update-failed'
        }
        """;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FlowCast/{CurrentVersion}");
        return client;
    }

    private static Version GetCurrentVersion(Assembly assembly) =>
        assembly.GetName().Version ?? new Version(0, 0);

    private static bool HasMatchingChecksum(string packagePath, string checksumText)
    {
        var expectedHash = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expectedHash is null)
        {
            return false;
        }

        try
        {
            var expectedBytes = Convert.FromHexString(expectedHash);
            var actualBytes = SHA256.HashData(File.ReadAllBytes(packagePath));
            return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void EnsureApplicationDirectoryIsWritable(string applicationDirectory)
    {
        var probePath = Path.Combine(applicationDirectory, $".flowcast-update-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probePath, string.Empty);
        File.Delete(probePath);
    }
}
