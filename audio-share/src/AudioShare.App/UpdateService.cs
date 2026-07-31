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

    public const string UpdateFailureSignal = "--update-failed";
    public const string UpdateFailedRestartNotice = "上一次更新未完成，已保留原程序并重新启动。";

    public UpdateService()
    {
    }

    public UpdateService(HttpClient client)
    {
        this.client = client;
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
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("找不到当前程序文件。");
        var applicationDirectory = Path.GetDirectoryName(executablePath) ?? throw new InvalidOperationException("找不到程序文件夹。");
        EnsureApplicationDirectoryIsWritable(applicationDirectory);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "FlowCast", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);

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

        var requiredFiles = new[] { "AudioShare.App.exe", "ThirdPartyNotices.txt" };
        if (requiredFiles.Any(fileName => !File.Exists(Path.Combine(extractedDirectory, fileName))))
        {
            throw new InvalidDataException("更新压缩包未包含预期的程序或授权说明文件。");
        }

        return new StagedUpdate(updateDirectory, extractedDirectory, executablePath);
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
        $Files = @("AudioShare.App.exe", "ThirdPartyNotices.txt")
        $MaximumAttempts = 5
        $ReplacementSucceeded = $false

        for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
            $ReplacedFiles = [System.Collections.Generic.List[string]]::new()
            try {
                foreach ($File in $Files) {
                    $SourcePath = Join-Path $SourceDirectory $File
                    $FileTargetPath = Join-Path $ApplicationDirectory $File
                    $StagedPath = "$FileTargetPath.flowcast-update-new"
                    $BackupPath = "$FileTargetPath.flowcast-update-backup"
                    Copy-Item -LiteralPath $SourcePath -Destination $StagedPath -Force
                    [System.IO.File]::Replace($StagedPath, $FileTargetPath, $BackupPath, $true)
                    $ReplacedFiles.Add($File)
                }

                foreach ($File in $Files) {
                    Remove-Item -LiteralPath (Join-Path $ApplicationDirectory "$File.flowcast-update-backup") -Force -ErrorAction SilentlyContinue
                }

                $ReplacementSucceeded = $true
                break
            }
            catch {
                foreach ($File in $ReplacedFiles) {
                    $FileTargetPath = Join-Path $ApplicationDirectory $File
                    $BackupPath = "$FileTargetPath.flowcast-update-backup"
                    if (Test-Path -LiteralPath $BackupPath) {
                        [System.IO.File]::Replace($BackupPath, $FileTargetPath, $null, $true)
                    }
                }

                foreach ($File in $Files) {
                    Remove-Item -LiteralPath (Join-Path $ApplicationDirectory "$File.flowcast-update-new") -Force -ErrorAction SilentlyContinue
                }

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
