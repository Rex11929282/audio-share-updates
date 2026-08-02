using System.IO;
using System.Runtime.CompilerServices;

namespace AudioShare.Core.Tests;

public sealed class FlowCastLyricsStartupTests
{
    [Fact]
    public void App_UsesMainWindowAsItsStartupWindow()
    {
        var app = new global::AudioShare.Lyrics.App();

        Assert.Equal(new Uri("MainWindow.xaml", UriKind.Relative), app.StartupUri);
    }

    [Fact]
    public void MainWindow_IsACompactTopmostCompositionOverlay()
    {
        var path = FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);
        var root = System.Xml.Linq.XDocument.Parse(xaml).Root!;

        Assert.Equal("None", root.Attribute("WindowStyle")?.Value);
        Assert.Equal("NoResize", root.Attribute("ResizeMode")?.Value);
        Assert.Equal("True", root.Attribute("Topmost")?.Value);
        Assert.Equal("True", root.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("180", root.Attribute("Width")?.Value);
        Assert.Equal("44", root.Attribute("Height")?.Value);
        Assert.Equal("180", root.Attribute("MinWidth")?.Value);
        Assert.Equal("44", root.Attribute("MinHeight")?.Value);
        Assert.Equal("680", root.Attribute("MaxWidth")?.Value);
        Assert.Equal("92", root.Attribute("MaxHeight")?.Value);
        Assert.Contains("WebView2CompositionControl", xaml);
        Assert.Contains("PreviewMouseLeftButtonDown", xaml);
        Assert.DoesNotContain("CloseButton", xaml);
        Assert.DoesNotContain("DragHandle", xaml);
        Assert.DoesNotContain("真實歌詞", xaml);
        Assert.DoesNotContain("正在尋找 FlowCast", xaml);
        Assert.DoesNotContain("Radmin VPN", xaml);
    }

    [Fact]
    public void MainWindow_KeepsTheNativeStatusVisibleUntilTheWebSurfaceIsReady()
    {
        var path = FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs");
        var source = File.ReadAllText(path);
        var readyHandler = source.IndexOf("type.GetString() == \"ready\"", StringComparison.Ordinal);
        var showWebSurface = source.IndexOf("OverlayWebView.Visibility = Visibility.Visible;", StringComparison.Ordinal);
        var hideFallback = source.IndexOf("NativeFallback.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);

        Assert.True(readyHandler >= 0);
        Assert.True(showWebSurface > readyHandler);
        Assert.True(hideFallback > readyHandler);
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
