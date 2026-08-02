using System.IO;

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
        Assert.Equal("680", root.Attribute("Width")?.Value);
        Assert.Equal("150", root.Attribute("Height")?.Value);
        Assert.Contains("WebView2CompositionControl", xaml);
        Assert.DoesNotContain("真實歌詞", xaml);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeSegments));
    }
}
