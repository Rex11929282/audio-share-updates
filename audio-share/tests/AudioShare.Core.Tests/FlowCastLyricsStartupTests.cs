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
    public void MainWindow_IsAnInvisibleNonUserFacingHost()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml"));
        var root = System.Xml.Linq.XDocument.Parse(xaml).Root!;

        Assert.Equal("None", root.Attribute("WindowStyle")?.Value);
        Assert.Equal("True", root.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("1", root.Attribute("Width")?.Value);
        Assert.Equal("1", root.Attribute("Height")?.Value);
        Assert.Equal("0", root.Attribute("Opacity")?.Value);
        Assert.Equal("False", root.Attribute("ShowInTaskbar")?.Value);
        Assert.Equal("False", root.Attribute("ShowActivated")?.Value);
        Assert.Equal("False", root.Attribute("Topmost")?.Value);
        Assert.Equal("NoResize", root.Attribute("ResizeMode")?.Value);
        Assert.DoesNotContain("WebView", xaml);
        Assert.DoesNotContain("NativeFallback", xaml);
        Assert.DoesNotContain("ContextMenu", xaml);
        Assert.DoesNotContain("TextBlock", xaml);
        Assert.DoesNotContain("WindowSurface", xaml);
    }

    [Fact]
    public void MainWindow_StartsHiddenGlassRendererAndConnectionOnlyRuntime()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));
        var loadedHandler = source.IndexOf("MainWindow_OnLoaded", StringComparison.Ordinal);
        var hide = source.IndexOf("Hide();", loadedHandler, StringComparison.Ordinal);
        var firstAwait = source.IndexOf("await ", loadedHandler, StringComparison.Ordinal);

        Assert.True(loadedHandler >= 0);
        Assert.True(hide > loadedHandler);
        Assert.True(firstAwait > hide);
        Assert.Contains("LyricsGlassSettingsStore.CreateDefault()", source);
        Assert.Contains("settingsStore.Load()", source);
        Assert.Contains("new LyricsGlassRendererSupervisor(hostState)", source);
        Assert.Contains("new LyricsConnectionRuntime(new RadminLyricsRelay(", source);
        Assert.Contains("renderer.StartAsync", source);
        Assert.Contains("connectionRuntime.StartAsync", source);
        Assert.DoesNotContain("new LyricsRuntime", source);
        Assert.DoesNotContain("Netease", source);
    }

    [Fact]
    public void MainWindow_ForwardsOnlyTruthfulConnectionStates()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));

        Assert.Contains("LyricsConnectionState.FindingFlowcast", source);
        Assert.Contains("LyricsGlassConnectionState.FindingFlowcast", source);
        Assert.Contains("LyricsConnectionState.ConnectedAwaitingLyrics", source);
        Assert.Contains("LyricsGlassConnectionState.ConnectedAwaitingLyrics", source);
        Assert.Contains("SetConnectionStateAsync", source);
        Assert.DoesNotContain("LyricLine", source);
        Assert.DoesNotContain("SnapshotChanged", source);
    }

    [Fact]
    public void MainWindow_DelegatesTypedGlassEventsToTheTestedPersistenceBoundary()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));

        Assert.Contains("LyricsGlassSettingsCommittedEventArgs", source);
        Assert.Contains("LyricsGlassPositionChangedEventArgs", source);
        Assert.Contains("LyricsGlassHostStatePersistence", source);
        Assert.Contains("hostStatePersistence.ApplySettings(eventArgs.Settings)", source);
        Assert.Contains("hostStatePersistence.ApplyPosition(eventArgs.Position)", source);
    }

    [Fact]
    public void MainWindow_DispatchesRendererCloseAndStopsHelperBeforeRadminAndFinalClose()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));
        var closeRequest = source.IndexOf("Renderer_OnCloseRequested", StringComparison.Ordinal);
        var shutdown = source.IndexOf("ShutdownAndCloseAsync", StringComparison.Ordinal);

        Assert.True(closeRequest >= 0);
        Assert.True(shutdown >= 0);

        var dispatcherClose = source.IndexOf("Dispatcher.BeginInvoke", closeRequest, StringComparison.Ordinal);
        var coordinatedShutdown = source.IndexOf("await shutdownCoordinator.ShutdownAsync()", shutdown, StringComparison.Ordinal);
        var allowFinalClose = source.IndexOf("finalCloseAllowed = true", shutdown, StringComparison.Ordinal);
        var finalClose = source.IndexOf("Dispatcher.InvokeAsync", shutdown, StringComparison.Ordinal);

        Assert.True(dispatcherClose > closeRequest);
        Assert.True(coordinatedShutdown > shutdown);
        Assert.True(allowFinalClose > coordinatedShutdown);
        Assert.True(finalClose > allowFinalClose);
        Assert.Contains("eventArgs.Cancel = true", source);
        Assert.Contains("closeCleanupStarted", source);
        Assert.Contains("LyricsHostShutdownCoordinator", source);
        Assert.DoesNotContain("closeCleanupTask", source);
        Assert.DoesNotContain("catch (Exception)", source);
    }

    [Fact]
    public void RadminRelay_RaisesConnectedOnlyAfterARealReceiverConnection()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "RadminLyricsRelay.cs"));
        var connectedReceiver = source.IndexOf(
            "if (await receiver.DiscoverAndConnectOnRadminAsync(cancellationToken))",
            StringComparison.Ordinal);
        var connectedEvent = source.IndexOf("Connected?.Invoke(this, EventArgs.Empty);", StringComparison.Ordinal);
        var waitForDisconnect = source.IndexOf("await disconnected.Task.WaitAsync(cancellationToken);", StringComparison.Ordinal);

        Assert.True(connectedReceiver >= 0);
        Assert.True(connectedEvent > connectedReceiver);
        Assert.True(waitForDisconnect > connectedEvent);
    }

    [Fact]
    public void LyricsProject_HasNoWebViewOrFrontendBuildInputs()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "AudioShare.Lyrics.csproj"));
        var removedPackage = "Microsoft.Web.Web" + "View2";
        var removedBuildTarget = "BuildLyrics" + "Frontend";

        Assert.DoesNotContain(removedPackage, project);
        Assert.DoesNotContain("LyricsFrontendSource", project);
        Assert.DoesNotContain(removedBuildTarget, project);
        Assert.DoesNotContain("CopyLyricsFrontendToOutput", project);
        Assert.DoesNotContain("CopyLyricsFrontendToPublish", project);
        Assert.Contains("ThirdPartyNotices.txt", project);
        Assert.Contains("InternalsVisibleTo", project);
    }

    [Fact]
    public void ReplacedVisualSourceAndTests_AreRemoved()
    {
        var root = FindRepositoryRoot();
        var backdropCapture = "Desktop" + "BackdropCapture";
        var tunerWindow = "LiquidGlass" + "TunerWindow";
        string[] removedPaths =
        [
            $"src/AudioShare.Lyrics/{backdropCapture}.cs",
            "src/AudioShare.Lyrics/WindowBackdrop.cs",
            "src/AudioShare.Lyrics/LyricsWebViewEnvironment.cs",
            $"src/AudioShare.Lyrics/{tunerWindow}.xaml",
            $"src/AudioShare.Lyrics/{tunerWindow}.xaml.cs",
            "src/AudioShare.Lyrics/LiquidGlassSettings.cs",
            "src/AudioShare.Lyrics/LiquidGlassSettingsController.cs",
            "src/AudioShare.Lyrics/LiquidGlassSettingsStore.cs",
            "src/AudioShare.Lyrics/LyricsOverlayPresenter.cs",
            "src/AudioShare.Lyrics/IslandDimensions.cs",
            "src/AudioShare.Lyrics/Frontend",
            $"tests/AudioShare.Core.Tests/{tunerWindow}Tests.cs",
            "tests/AudioShare.Core.Tests/LiquidGlassSettingsTests.cs",
            "tests/AudioShare.Core.Tests/LiquidGlassSettingsControllerTests.cs",
            "tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs",
            "tests/AudioShare.Core.Tests/IslandDimensionsTests.cs"
        ];

        foreach (var relativePath in removedPaths)
        {
            Assert.False(
                File.Exists(Path.Combine(root, relativePath)) || Directory.Exists(Path.Combine(root, relativePath)),
                $"Replaced visual path still exists: {relativePath}");
        }
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

        var startingDirectories = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in startingDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
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
