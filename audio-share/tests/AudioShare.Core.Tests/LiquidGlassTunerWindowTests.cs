using System.IO;
using System.Runtime.CompilerServices;

namespace AudioShare.Core.Tests;

public sealed class LiquidGlassTunerWindowTests
{
    [Fact]
    public void TunerWindow_LoadsTheReactTunerAndHandlesEveryCommand()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));

        Assert.Contains("index.html?surface=tuner", source);
        Assert.Contains("\"tuner-ready\"", source);
        Assert.Contains("\"liquid-preview\"", source);
        Assert.Contains("\"liquid-reset\"", source);
        Assert.Contains("\"liquid-cancel\"", source);
        Assert.Contains("\"liquid-save\"", source);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var sourceDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(sourceDirectory, "..", ".."));
        var sourceCandidate = Path.Combine([repositoryRoot, .. relativeSegments]);
        if (File.Exists(sourceCandidate))
        {
            return sourceCandidate;
        }

        var startingDirectories = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in startingDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
