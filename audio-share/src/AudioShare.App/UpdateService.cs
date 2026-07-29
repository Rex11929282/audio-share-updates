using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using AudioShare.Core;

namespace AudioShare.App;

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Rex11929282/audio-share-updates/releases/latest";
    private static readonly Version CurrentVersion = new(1, 0, 0);

    public async Task<ReleaseUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var releaseJson = await client.GetStringAsync(LatestReleaseUrl, cancellationToken);
        return ReleaseUpdateParser.TryParseNewerRelease(releaseJson, CurrentVersion);
    }

    public async Task DownloadVerifyAndRestartAsync(ReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("找不到目前的程式檔案。");
        var applicationDirectory = Path.GetDirectoryName(executablePath) ?? throw new InvalidOperationException("找不到程式資料夾。");
        EnsureApplicationDirectoryIsWritable(applicationDirectory);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "AudioShare", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);

        var packagePath = Path.Combine(updateDirectory, ReleaseUpdateParser.PackageAssetName);
        var checksumPath = Path.Combine(updateDirectory, ReleaseUpdateParser.ChecksumAssetName);

        using (var client = CreateClient())
        {
            await File.WriteAllBytesAsync(packagePath, await client.GetByteArrayAsync(update.AssetUrl, cancellationToken), cancellationToken);
            await File.WriteAllBytesAsync(checksumPath, await client.GetByteArrayAsync(update.Sha256Url, cancellationToken), cancellationToken);
        }

        if (!HasMatchingChecksum(packagePath, await File.ReadAllTextAsync(checksumPath, cancellationToken)))
        {
            throw new InvalidDataException("更新檔案的 SHA-256 驗證失敗。");
        }

        var extractedDirectory = Path.Combine(updateDirectory, "extracted");
        ZipFile.ExtractToDirectory(packagePath, extractedDirectory);

        var replacementPath = Path.Combine(extractedDirectory, Path.GetFileName(executablePath));
        if (!File.Exists(replacementPath))
        {
            throw new InvalidDataException("更新壓縮檔未包含預期的程式檔案。");
        }

        var scriptPath = Path.Combine(updateDirectory, "replace-and-restart.ps1");
        await File.WriteAllTextAsync(scriptPath, ReplacementScript, new UTF8Encoding(false), cancellationToken);

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
                replacementPath,
                executablePath,
            },
        });

        if (process is null)
        {
            throw new InvalidOperationException("無法啟動更新程式。");
        }
    }

    private static readonly string ReplacementScript = """
        param(
            [int]$ProcessId,
            [string]$SourcePath,
            [string]$TargetPath
        )

        Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath $SourcePath -Destination $TargetPath -Force
        Start-Process -FilePath $TargetPath
        """;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AudioShare/1.0.0");
        return client;
    }

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
        var probePath = Path.Combine(applicationDirectory, $".audioshare-update-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probePath, string.Empty);
        File.Delete(probePath);
    }
}
