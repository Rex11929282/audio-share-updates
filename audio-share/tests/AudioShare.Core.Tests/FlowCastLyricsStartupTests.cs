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

    [Fact]
    public void MainWindow_FeedsTheDesktopBehindTheIslandIntoTheWebGlass()
    {
        var xamlPath = FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml");
        var sourcePath = FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.Contains("WindowBackdrop.TryExcludeFromCapture(this)", source);
        Assert.Contains("desktopBackdrop.Capture", source);
        Assert.Contains("PostWebMessageAsJson", source);
        Assert.Contains("type = \"backdrop\"", source);
    }

    [Fact]
    public void MainWindow_BoundsBackdropCaptureRetriesAfterReadyOrLayoutTriggers()
    {
        var sourcePath = FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private const int MaximumBackdropRetries = 2;", source);
        Assert.Contains("RefreshDesktopBackdrop();", source);
        Assert.Contains("LocationChanged += (_, _) => ScheduleBackdropRefresh();", source);
        Assert.Contains("SizeChanged += (_, _) => ScheduleBackdropRefresh();", source);
        Assert.Contains("backdropRetryAttempts = 0;", source);
        Assert.Contains("if (backdropRetryAttempts >= MaximumBackdropRetries)", source);
        Assert.Contains("backdropRetryAttempts++;", source);
        Assert.Contains("ScheduleBackdropRetry();", source);
    }

    [Fact]
    public void MainWindow_RightClickOpensTheLiquidGlassOptionMenu()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml"));
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));

        Assert.Contains("PreviewMouseRightButtonUp=\"WindowSurface_OnPreviewMouseRightButtonUp\"", xaml);
        Assert.Contains("x:Name=\"CapsuleContextMenu\"", xaml);
        Assert.Contains("Header=\"Adjust Liquid Glass…\"", xaml);
        Assert.Contains("Header=\"Close FlowCast Lyrics\"", xaml);
        Assert.Contains("Click=\"AdjustLiquidGlass_OnClick\"", xaml);
        Assert.Contains("Click=\"CloseFlowCastLyrics_OnClick\"", xaml);
        Assert.Contains("CapsuleContextMenu.IsOpen = true;", source);
        Assert.Contains("private void AdjustLiquidGlass_OnClick", source);
        Assert.Contains("private void CloseFlowCastLyrics_OnClick", source);
        Assert.Contains("LiquidGlassTunerWindow? tunerWindow", source);
        Assert.Contains("tunerWindow.Activate()", source);
    }

    [Fact]
    public void MainWindow_CloseClosesTheTunerBeforeOverlayTeardown()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));
        var closeStart = source.IndexOf("private void MainWindow_OnClosed", StringComparison.Ordinal);
        var closeEnd = source.IndexOf("private const uint MonitorDefaultToNearest", closeStart, StringComparison.Ordinal);
        var closeHandler = source[closeStart..closeEnd];
        var copyTuner = closeHandler.IndexOf("var tuner = tunerWindow;", StringComparison.Ordinal);
        var clearTuner = closeHandler.IndexOf("tunerWindow = null;", StringComparison.Ordinal);
        var detachTuner = closeHandler.IndexOf("tuner.Closed -= TunerWindow_OnClosed;", StringComparison.Ordinal);
        var closeTuner = closeHandler.IndexOf("tuner.Close();", StringComparison.Ordinal);
        var disposeOverlay = closeHandler.IndexOf("OverlayWebView.Dispose();", StringComparison.Ordinal);

        Assert.True(copyTuner >= 0);
        Assert.True(clearTuner > copyTuner);
        Assert.True(detachTuner > clearTuner);
        Assert.True(closeTuner > detachTuner);
        Assert.True(disposeOverlay > closeTuner);
        Assert.Contains("ReferenceEquals(tunerWindow, closedTuner)", source);
        Assert.DoesNotContain("Owner =", source);
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
