using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace AudioShare.Core.Tests;

public sealed class LyricsGlassPackagingTests
{
    private static readonly string PackagingTestTempRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "FlowCast.Lyrics.PackageTests"));

    [Fact]
    public void LyricsProject_BuildsCompleteGlassImageOnlyForOptInPublish()
    {
        var project = XDocument.Load(FindRepositoryFile("src", "AudioShare.Lyrics", "AudioShare.Lyrics.csproj"));
        var target = project.Descendants("Target").Single(element =>
            string.Equals((string?)element.Attribute("Name"), "BuildLyricsGlassRenderer", StringComparison.Ordinal));

        Assert.Equal("Publish", (string?)target.Attribute("BeforeTargets"));
        Assert.Contains("BuildLyricsGlassRenderer", (string?)target.Attribute("Condition"));
        Assert.Contains("true", (string?)target.Attribute("Condition"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Build", ((string?)target.Attribute("BeforeTargets"))!.Split(';'));

        var command = (string?)target.Descendants("Exec").Single().Attribute("Command");
        Assert.Contains("build-lyrics-glass.ps1", command);
        Assert.Contains("$(PublishDir)", command);
        Assert.Contains("$(PublishDir).", command);
        Assert.Contains("System.IO.Path]::GetFullPath", command);
        Assert.Contains("$(MSBuildProjectDirectory)", command);

        var script = File.ReadAllText(FindRepositoryFile("scripts", "build-lyrics-glass.ps1"));
        Assert.Contains("createDistributable", script);
        Assert.Contains("--no-daemon", script);
        Assert.Contains("FlowCast Lyrics Glass.exe", script);
        Assert.Contains("Copy-Item", script);
        Assert.Contains("-Recurse", script);
        Assert.Contains("'glass'", script);
        Assert.Contains("GetFullPath", script);
    }

    [Fact]
    public void LyricsPublishScript_ProducesASeparateProductZipAndChecksum()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "publish-lyrics.ps1"));

        Assert.Contains("AudioShare.Lyrics.csproj", script);
        Assert.Contains("--self-contained", script);
        Assert.Contains("PublishSingleFile=true", script);
        Assert.Contains("BuildLyricsGlassRenderer=true", script);
        Assert.Contains("FlowCast-Lyrics-win-x64.zip", script);
        Assert.Contains("FlowCast-Lyrics-win-x64.zip.sha256", script);
        Assert.Contains("Get-FileHash", script);
        Assert.DoesNotMatch(@"(?m)^\s*&[^\r\n]*publish-release\.ps1", script);
        Assert.DoesNotContain("AudioShare.App.csproj", script);
        Assert.DoesNotContain("makensis", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installer\\FlowCast", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?m)^\s*&[^\r\n]*build-router-helper\.ps1", script);
    }

    [Fact]
    public void LyricsPublishScript_RejectsForbiddenPayloads()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "publish-lyrics.ps1"));
        var removedWebView = "Web" + "View2";
        var removedReactGlass = "liquid-glass-" + "react";

        Assert.Contains("AudioShare.App.exe", script);
        Assert.Contains("router-helper", script);
        Assert.Contains(removedWebView, script);
        Assert.Contains("React", script);
        Assert.Contains(removedReactGlass, script);
        Assert.Contains("FlowCast-Setup", script);
        Assert.Contains("throw", script);
    }

    [Fact]
    public async Task BuildScript_WithPreparedImage_CopiesTheCompleteTree()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var image = CreateFakeGlassImage(Path.Combine(root, "image"));
            File.WriteAllText(Path.Combine(image, "app", "nested.txt"), "nested");
            var destination = Path.Combine(root, "destination");

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "build-lyrics-glass.ps1"),
                "-DestinationDirectory", destination,
                "-SourceAppImageDirectory", image);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(destination, "glass", "FlowCast Lyrics Glass.exe")));
            Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "glass", "app", "nested.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "glass", "runtime", "bin", "java.exe")));
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task BuildScript_RejectsAReparseDescendantBeforeReplacingGlass()
    {
        var root = CreateTemporaryDirectory();
        var junction = Path.Combine(root, "destination", "glass", "escape");
        try
        {
            var image = CreateFakeGlassImage(Path.Combine(root, "image"));
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            var sentinel = Path.Combine(outside, "sentinel.txt");
            File.WriteAllText(sentinel, "keep");
            Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
            CreateJunction(junction, outside);

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "build-lyrics-glass.ps1"),
                "-DestinationDirectory", Path.Combine(root, "destination"),
                "-SourceAppImageDirectory", image);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.AllOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally
        {
            DeleteJunctionIfPresent(junction);
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task BuildScript_RejectsAReparseDestinationAncestor()
    {
        var root = CreateTemporaryDirectory();
        var alias = Path.Combine(root, "alias");
        try
        {
            var image = CreateFakeGlassImage(Path.Combine(root, "image"));
            var real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            CreateJunction(alias, real);

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "build-lyrics-glass.ps1"),
                "-DestinationDirectory", Path.Combine(alias, "destination"),
                "-SourceAppImageDirectory", image);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.AllOutput, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(real, "destination", "glass")));
        }
        finally
        {
            DeleteJunctionIfPresent(alias);
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task PublishScript_WithPreparedPackage_CreatesZipAndMatchingChecksum()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var prepared = CreateFakePublishedProduct(Path.Combine(root, "prepared"));
            var output = Path.Combine(root, "output");

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "publish-lyrics.ps1"),
                "-OutputDirectory", output,
                "-PreparedPublishDirectory", prepared);

            Assert.Equal(0, result.ExitCode);
            var zipPath = Path.Combine(output, "FlowCast-Lyrics-win-x64.zip");
            var checksumPath = zipPath + ".sha256";
            var expectedHash = File.ReadAllText(checksumPath).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            Assert.Equal(expectedHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipPath))), ignoreCase: true);
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
            Assert.Contains("FlowCast Lyrics.exe", entries);
            Assert.Contains("glass/FlowCast Lyrics Glass.exe", entries);
            Assert.Contains(entries, entry => entry.StartsWith("glass/app/", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.StartsWith("glass/runtime/", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task PublishScript_RejectsForbiddenTextInsideANeutralAssetName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var prepared = CreateFakePublishedProduct(Path.Combine(root, "prepared"));
            File.WriteAllText(Path.Combine(prepared, "glass", "app", "asset-a1b2c3.js"), "const renderer = 'React';");

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "publish-lyrics.ps1"),
                "-OutputDirectory", Path.Combine(root, "output"),
                "-PreparedPublishDirectory", prepared);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Forbidden content", result.AllOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task PublishScript_RejectsForbiddenTextAcrossChunksInALargeNeutralAssetName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var prepared = CreateFakePublishedProduct(Path.Combine(root, "prepared"));
            var largeAsset = new string('x', (81 * 65536) - 2) + "React";
            File.WriteAllText(Path.Combine(prepared, "glass", "app", "asset-large-a1b2c3.js"), largeAsset);

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "publish-lyrics.ps1"),
                "-OutputDirectory", Path.Combine(root, "output"),
                "-PreparedPublishDirectory", prepared);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Forbidden content", result.AllOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task PublishScript_RejectsAForbiddenFilenameInThePreparedPackage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var prepared = CreateFakePublishedProduct(Path.Combine(root, "prepared"));
            File.WriteAllText(Path.Combine(prepared, "AudioShare.App.exe"), "forbidden");

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "publish-lyrics.ps1"),
                "-OutputDirectory", Path.Combine(root, "output"),
                "-PreparedPublishDirectory", prepared);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Forbidden payload", result.AllOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public async Task PublishScript_RejectsAReparseDescendantBeforeCleaningOutput()
    {
        var root = CreateTemporaryDirectory();
        var junction = Path.Combine(root, "output", "publish", "escape");
        try
        {
            var prepared = CreateFakePublishedProduct(Path.Combine(root, "prepared"));
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
            var sentinel = Path.Combine(outside, "sentinel.txt");
            File.WriteAllText(sentinel, "keep");
            Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
            CreateJunction(junction, outside);

            var result = await RunPowerShellScriptAsync(
                FindRepositoryFile("scripts", "publish-lyrics.ps1"),
                "-OutputDirectory", Path.Combine(root, "output"),
                "-PreparedPublishDirectory", prepared);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.AllOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally
        {
            DeleteJunctionIfPresent(junction);
            DeleteDirectoryIfPresent(root);
        }
    }

    [Fact]
    public void TestCleanup_RejectsAReparseDescendant()
    {
        var root = CreateTemporaryDirectory();
        var junction = Path.Combine(root, "escape");
        var outside = CreateTemporaryDirectory();
        var sentinel = Path.Combine(outside, "sentinel.txt");
        try
        {
            File.WriteAllText(sentinel, "keep");
            CreateJunction(junction, outside);

            var exception = Assert.Throws<InvalidOperationException>(() => DeleteDirectoryIfPresent(root));

            Assert.Contains("reparse", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally
        {
            DeleteJunctionIfPresent(junction);
            DeleteDirectoryIfPresent(root);
            DeleteDirectoryIfPresent(outside);
        }
    }

    [Fact]
    public void ThirdPartyNotices_AttributeBackdropAndShapesWithApacheLicense()
    {
        var notices = File.ReadAllText(FindRepositoryFile("ThirdPartyNotices.txt"));
        var removedWebView = "Microsoft.Web.Web" + "View2";
        var removedReactGlass = "liquid-glass-" + "react";

        Assert.Contains("AndroidLiquidGlass / Backdrop 2.0.0", notices);
        Assert.Contains("io.github.kyant0:backdrop:2.0.0", notices);
        Assert.Contains("Shapes 1.2.0", notices);
        Assert.Contains("io.github.kyant0:shapes:1.2.0", notices);
        Assert.Contains("Apache License", notices);
        Assert.Contains("Version 2.0, January 2004", notices);
        Assert.Contains("END OF TERMS AND CONDITIONS", notices);
        Assert.DoesNotContain(removedWebView, notices);
        Assert.DoesNotContain(removedReactGlass, notices);
        Assert.DoesNotContain("React and React DOM", notices);
    }

    [Fact]
    public void Readme_DescribesLyricsAsAnHonestSeparateProduct()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));

        Assert.Contains("FlowCast Lyrics.exe", readme);
        Assert.Contains("separately packaged", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separately updated", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same Radmin VPN", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatically", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draggable", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Adjust Glass", readme);
        Assert.Contains("Close FlowCast Lyrics", readme);
        Assert.Contains("does not provide a lyric source", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not ask for an IP address, port, pairing code, or QR code", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not require a separately installed Java runtime", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installer includes FlowCast and FlowCast Lyrics", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var candidate = Path.Combine([FindRepositoryRoot(), .. relativeSegments]);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeSegments));
    }

    private static string FindRepositoryRoot()
    {
        var sourceDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var sourceRoot = Path.GetFullPath(Path.Combine(sourceDirectory, "..", ".."));
        if (Directory.Exists(Path.Combine(sourceRoot, "src", "AudioShare.Lyrics")))
        {
            return sourceRoot;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "AudioShare.Lyrics")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the audio-share repository root.");
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private static string CreateTemporaryDirectory()
    {
        Directory.CreateDirectory(PackagingTestTempRoot);
        AssertNoReparseAncestors(PackagingTestTempRoot);
        var path = Path.Combine(PackagingTestTempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateFakeGlassImage(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "app"));
        Directory.CreateDirectory(Path.Combine(path, "runtime", "bin"));
        File.WriteAllText(Path.Combine(path, "FlowCast Lyrics Glass.exe"), "helper");
        File.WriteAllText(Path.Combine(path, "app", "renderer.jar"), "renderer");
        File.WriteAllText(Path.Combine(path, "runtime", "bin", "java.exe"), "java");
        return path;
    }

    private static string CreateFakePublishedProduct(string path)
    {
        CreateFakeGlassImage(Path.Combine(path, "glass"));
        File.WriteAllText(Path.Combine(path, "FlowCast Lyrics.exe"), "host");
        File.WriteAllText(Path.Combine(path, "ThirdPartyNotices.txt"), "Apache License 2.0");
        return path;
    }

    private static async Task<ScriptResult> RunPowerShellScriptAsync(string scriptPath, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("/d");
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("mklink");
        process.StartInfo.ArgumentList.Add("/J");
        process.StartInfo.ArgumentList.Add(junctionPath);
        process.StartInfo.ArgumentList.Add(targetPath);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Could not create test junction: {output}{error}");
    }

    private static void DeleteJunctionIfPresent(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            Assert.True(
                (attributes & FileAttributes.ReparsePoint) != 0,
                $"Refusing test cleanup for a non-reparse directory: {path}");
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        Directory.Delete(path, recursive: false);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var tempPrefix = PackagingTestTempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing test cleanup outside the packaging temp root: {fullPath}");
        }

        if (!Directory.Exists(fullPath))
        {
            return;
        }

        AssertNoReparseAncestors(fullPath);
        AssertNoReparseTree(fullPath);
        Directory.Delete(fullPath, recursive: true);
    }

    private static void AssertNoReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Test cleanup path contains a reparse-point ancestor: {directory.FullName}");
            }
        }
    }

    private static void AssertNoReparseTree(string path)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(Path.GetFullPath(path)));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Test cleanup path contains a reparse point: {current.FullName}");
            }

            foreach (var child in current.EnumerateFileSystemInfos())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"Test cleanup path contains a reparse point: {child.FullName}");
                }

                if (child is DirectoryInfo directory)
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;
    }
}
