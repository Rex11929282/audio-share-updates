using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace AudioShare.Core.Tests;

public sealed class LyricsGlassPackagingTests
{
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
}
