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
    public void TunerWindow_DefaultViewportShowsTheFullPanel()
    {
        var xaml = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml"));
        var root = XDocument.Parse(xaml).Root!;

        Assert.Equal("720", root.Attribute("Width")?.Value);
        Assert.Equal("720", root.Attribute("Height")?.Value);
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
    public void TunerWindow_CancelPreventsQueuedSettingsFromTouchingADisposedWebView()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));
        var cancelCase = source.IndexOf("case \"liquid-cancel\":", StringComparison.Ordinal);
        var cancelSection = source[cancelCase..source.IndexOf("case \"liquid-save\":", cancelCase, StringComparison.Ordinal)];
        var onClosing = source.IndexOf("private void OnClosing", StringComparison.Ordinal);
        var onClosingSection = source[onClosing..source.IndexOf("private void OnClosed", onClosing, StringComparison.Ordinal)];
        var settingsChanged = source.IndexOf("private void Controller_OnSettingsChanged", StringComparison.Ordinal);
        var settingsChangedSection = source[settingsChanged..source.IndexOf("private void PostSettings", settingsChanged, StringComparison.Ordinal)];
        var postSettings = source.IndexOf("private void PostSettings", StringComparison.Ordinal);
        var postSettingsSection = source[postSettings..source.IndexOf("private void OnClosing", postSettings, StringComparison.Ordinal)];

        Assert.Contains("private bool isClosing;", source);
        Assert.True(cancelSection.IndexOf("isClosing = true;", StringComparison.Ordinal) < cancelSection.IndexOf("controller.Cancel();", StringComparison.Ordinal));
        Assert.True(onClosingSection.IndexOf("isClosing = true;", StringComparison.Ordinal) < onClosingSection.IndexOf("controller.Cancel();", StringComparison.Ordinal));
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", settingsChangedSection);
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", postSettingsSection);
        Assert.True(settingsChangedSection.IndexOf("if (isClosing)", StringComparison.Ordinal) < settingsChangedSection.IndexOf("Dispatcher.BeginInvoke(PostSettings)", StringComparison.Ordinal));
        Assert.True(postSettingsSection.IndexOf("if (isClosing)", StringComparison.Ordinal) < postSettingsSection.IndexOf("TunerWebView.CoreWebView2", StringComparison.Ordinal));
    }

    [Fact]
    public void TunerWindow_SaveClosePreventsQueuedSettingsFromTouchingADisposedWebView()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));
        var saveCase = source.IndexOf("case \"liquid-save\":", StringComparison.Ordinal);
        var saveSection = source[saveCase..source.IndexOf("catch (JsonException)", saveCase, StringComparison.Ordinal)];
        var postSettings = source.IndexOf("private void PostSettings", StringComparison.Ordinal);
        var postSettingsSection = source[postSettings..source.IndexOf("private void OnClosing", postSettings, StringComparison.Ordinal)];

        Assert.Contains("isClosing = true;", saveSection);
        Assert.True(saveSection.IndexOf("isClosing = true;", StringComparison.Ordinal) < saveSection.IndexOf("closeCommitted = true;", StringComparison.Ordinal));
        Assert.True(saveSection.IndexOf("isClosing = true;", StringComparison.Ordinal) < saveSection.IndexOf("Close();", StringComparison.Ordinal));
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", postSettingsSection);
        Assert.True(postSettingsSection.IndexOf("if (isClosing)", StringComparison.Ordinal) < postSettingsSection.IndexOf("TunerWebView.CoreWebView2", StringComparison.Ordinal));
    }

    [Fact]
    public void TunerWindow_CloseDuringAsyncInitializationDoesNotTouchDisposedWebViewOrShowFailure()
    {
        var source = File.ReadAllText(
            FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));
        var loaded = source.IndexOf("private async void OnLoaded", StringComparison.Ordinal);
        var initialization = source.IndexOf("var environment = await LyricsWebViewEnvironment.GetAsync();", loaded, StringComparison.Ordinal);
        var ensure = source.IndexOf("await TunerWebView.EnsureCoreWebView2Async(environment);", initialization, StringComparison.Ordinal);
        var setup = source.IndexOf("stage = \"setup\";", ensure, StringComparison.Ordinal);
        var navigation = source.IndexOf("private void OnNavigationCompleted", StringComparison.Ordinal);
        var timeout = source.IndexOf("private void OnTunerReadyTimedOut", StringComparison.Ordinal);
        var failure = source.IndexOf("private void ShowTunerLoadFailure", StringComparison.Ordinal);
        var guard = source.IndexOf("private void StopTunerReadinessGuard", failure, StringComparison.Ordinal);
        var closed = source.IndexOf("private void OnClosed", StringComparison.Ordinal);

        Assert.True(initialization >= 0);
        Assert.True(ensure > initialization);
        Assert.True(setup > ensure);
        Assert.Contains("if (isClosing)\n            {\n                return;\n            }", source[initialization..ensure]);
        Assert.Contains("if (isClosing)\n            {\n                return;\n            }", source[ensure..setup]);
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", source[navigation..timeout]);
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", source[timeout..failure]);
        Assert.Contains("if (isClosing)\n        {\n            return;\n        }", source[failure..guard]);
        Assert.True(source.IndexOf("isClosing = true;", closed, StringComparison.Ordinal) > closed);
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
