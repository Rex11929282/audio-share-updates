using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;

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
        Assert.Contains("GetCommandType(root)", source);
    }

    [Fact]
    public void TunerWindow_UsesTheSharedWebViewEnvironmentAndTracesInitializationFailures()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));

        Assert.Contains("LyricsWebViewEnvironment.GetAsync()", source);
        Assert.Contains("EnsureCoreWebView2Async(environment)", source);
        Assert.Contains("diagnosticLog.WriteTunerInitializationFailure(stage, exception);", source);
        var diagnosticWrite = source.IndexOf("diagnosticLog.WriteTunerInitializationFailure(stage, exception);", StringComparison.Ordinal);
        var trace = source.IndexOf("Trace.TraceError", StringComparison.Ordinal);
        var status = source.IndexOf("TunerStatus.Text = \"Unable to load the Liquid Glass tuner.\";", StringComparison.Ordinal);

        Assert.True(diagnosticWrite >= 0);
        Assert.True(trace > diagnosticWrite);
        Assert.True(status > diagnosticWrite);
        Assert.Contains("Trace.TraceError", source);
        Assert.DoesNotContain("catch (Exception)\n        {\n            TunerStatus.Text", source);
    }

    [Fact]
    public void TunerWindow_FailsNavigationAndTimesOutWhenTheReactTunerNeverSignalsReady()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));

        Assert.Contains("CoreWebView2.NavigationCompleted += OnNavigationCompleted;", source);
        Assert.Contains("private void OnNavigationCompleted", source);
        Assert.Contains("if (!e.IsSuccess)", source);
        Assert.Contains("ShowTunerLoadFailure(", source);
        Assert.Contains("\"navigation\",", source);
        Assert.Contains("TimeSpan.FromSeconds(10)", source);
        Assert.Contains("tunerReadyTimer.Start();", source);
        Assert.Contains("private void OnTunerReadyTimedOut", source);
        Assert.Contains("\"readiness\",", source);
        Assert.Contains("private void ShowTunerLoadFailure", source);
        Assert.Contains("TunerWebView.Visibility = Visibility.Hidden;", source);
        Assert.Contains("tunerReadyTimer.Stop();", source);
        Assert.Contains("TunerWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;", source);
    }

    [Fact]
    public void TunerWindow_IsA720By640NormalWindow()
    {
        var xaml = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml"));
        var root = XDocument.Parse(xaml).Root!;

        Assert.Equal("720", root.Attribute("Width")?.Value);
        Assert.Equal("640", root.Attribute("Height")?.Value);
        Assert.Null(root.Attribute("Owner"));
        Assert.Null(root.Attribute("WindowStyle"));
    }

    [Fact]
    public void TunerWindow_UsesTheSameCompositionControlAsTheOverlay()
    {
        var xaml = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml"));

        Assert.Contains("<wv2:WebView2CompositionControl x:Name=\"TunerWebView\"", xaml);
        Assert.DoesNotContain("<wv2:WebView2 x:Name=\"TunerWebView\"", xaml);
    }

    [Fact]
    public void TunerWindow_UnsubscribesAndDisposesOnClose()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));

        Assert.Contains("controller.SettingsChanged -= Controller_OnSettingsChanged;", source);
        Assert.Contains("TunerWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;", source);
        Assert.Contains("TunerWebView.Dispose();", source);
    }

    [Fact]
    public void TunerWindow_SaveProtectsExpectedPersistenceFailuresBeforeCommitAndClose()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));
        var saveCase = source.IndexOf("case \"liquid-save\":", StringComparison.Ordinal);
        var saveSection = source[saveCase..source.IndexOf("catch (JsonException)", saveCase, StringComparison.Ordinal)];
        var save = saveSection.IndexOf("controller.Save();", StringComparison.Ordinal);
        var ioCatch = saveSection.IndexOf("catch (IOException)", StringComparison.Ordinal);
        var accessCatch = saveSection.IndexOf("catch (UnauthorizedAccessException)", StringComparison.Ordinal);
        var committed = saveSection.IndexOf("closeCommitted = true;", StringComparison.Ordinal);
        var close = saveSection.IndexOf("Close();", StringComparison.Ordinal);

        Assert.True(save >= 0);
        Assert.True(ioCatch > save);
        Assert.True(accessCatch > save);
        Assert.True(committed > ioCatch);
        Assert.True(committed > accessCatch);
        Assert.True(close > committed);
        Assert.Contains("ShowSaveFailure();", saveSection);
        Assert.DoesNotContain("catch (Exception)", saveSection);
    }

    [Fact]
    public void GetCommandType_RejectsANonStringTypeWithoutThrowing()
    {
        using var message = JsonDocument.Parse("""{ "type": 1 }""");

        Assert.Null(global::AudioShare.Lyrics.LiquidGlassTunerWindow.GetCommandType(message.RootElement));
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
