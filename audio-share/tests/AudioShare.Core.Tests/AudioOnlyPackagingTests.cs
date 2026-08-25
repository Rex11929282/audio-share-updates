using System.IO;
using System.Runtime.CompilerServices;

namespace AudioShare.Core.Tests;

public sealed class AudioOnlyPackagingTests
{
    [Fact]
    public void MainWindow_UsesCustomChromeWithoutTheNativeResizeGrip()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MainWindow.xaml"));

        Assert.Contains("WindowStyle=\"None\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.WindowChrome", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeMode=\"CanResizeWithGrip\"", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesTheAppleGlassVisualSystemWithoutNativeFocusOutlines()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MainWindow.xaml"));

        Assert.Contains("x:Key=\"AppleCanvasBrush\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AppleGlassCard\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ApplePrimaryButton\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"ComboBoxItem\">", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusVisualStyle=\"{StaticResource", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchOverlay_DrawsTheBrandMarkWithGrowingStrokesEndingCrisp()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MainWindow.xaml"));
        var motion = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MotionController.cs"));

        // The launch mark is drawn by self-growing strokes, not a blurred equalizer stack.
        Assert.Contains("x:Name=\"LaunchFlow\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("StrokeDashCap=\"Round\"", mainWindow, StringComparison.Ordinal);

        // No launch blur and no legacy equalizer bars, so the finished mark stays crisp.
        Assert.DoesNotContain("x:Name=\"LaunchBrandBlur\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"LaunchWaveOne\"", mainWindow, StringComparison.Ordinal);

        // The draw-on effect animates the stroke dash offset and settles as a solid line.
        Assert.Contains("StrokeDashOffset", motion, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationRoutePicker_CapsTheEndpointDropdownSoItCannotFillTheScreen()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MainWindow.xaml"));

        // A machine with many audio endpoints must not open a route list that fills the screen.
        Assert.Contains("MaxDropDownHeight", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingText_IsTraditionalChineseNotSimplified()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine("src", "AudioShare.App", "MainWindow.xaml"),
            Path.Combine("src", "AudioShare.App", "MainWindow.xaml.cs"),
            Path.Combine("src", "AudioShare.App", "TutorialContent.cs"),
            Path.Combine("src", "AudioShare.App", "UpdateService.cs"),
        };

        // Characters whose Traditional and Simplified forms differ; none should remain simplified.
        var simplifiedOnly = new[] { '开', '关', '确', '声', '设', '听', '继', '检', '断', '启', '应', '历', '复', '发' };
        foreach (var relativePath in files)
        {
            var content = File.ReadAllText(Path.Combine(root, relativePath));
            foreach (var simplified in simplifiedOnly)
            {
                Assert.DoesNotContain(simplified.ToString(), content, StringComparison.Ordinal);
            }
        }

        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "MainWindow.xaml"));
        Assert.Contains("開始分享", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogShell_UsesTheSameLightweightCustomWindowLanguage()
    {
        var root = FindRepositoryRoot();
        var dialogShell = File.ReadAllText(Path.Combine(root, "src", "AudioShare.App", "FlowCastDialogWindow.cs"));

        Assert.Contains("Color.FromRgb(247, 249, 253)", dialogShell, StringComparison.Ordinal);
        Assert.Contains("Color.FromArgb(236, 255, 255, 255)", dialogShell, StringComparison.Ordinal);
        Assert.Contains("WindowStyle = WindowStyle.None", dialogShell, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuild_UsesTheSourceManagedRouterRuntimeWithoutHostPython()
    {
        var root = FindRepositoryRoot();
        var routerBuild = File.ReadAllText(Path.Combine(root, "scripts", "build-router-helper.ps1"));
        var releaseBuild = File.ReadAllText(Path.Combine(root, "scripts", "publish-release.ps1"));

        Assert.True(File.Exists(Path.Combine(root, "router-helper", "runtime", "flowcast-router-runtime.zip")));
        Assert.Contains("flowcast-router-runtime.zip", releaseBuild, StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter(Mandatory)]\n    [string]$PythonEmbedZip", routerBuild.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("Host Python 3.12 is required", routerBuild, StringComparison.Ordinal);
    }

    [Fact]
    public void RouterHelper_ReportsTheSameVersionAsThePackagedRouter()
    {
        var root = FindRepositoryRoot();
        var helper = File.ReadAllText(Path.Combine(root, "router-helper", "audio_share_router_helper.py"));
        var routerBuild = File.ReadAllText(Path.Combine(root, "scripts", "build-router-helper.ps1"));

        Assert.Contains("ROUTER_VERSION = \"1.2.1\"", helper, StringComparison.Ordinal);
        Assert.Contains("$routerVersion = '1.2.1'", routerBuild, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_ReturnsFailureWhenFlowCastCannotBeReplaced()
    {
        var root = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(root, "installer", "FlowCast.nsi"));

        Assert.Contains("SetErrorLevel 1", installer, StringComparison.Ordinal);
        Assert.Contains("tasklist.exe", installer, StringComparison.Ordinal);
        Assert.Contains("IfSilent silentLocked interactiveLocked", installer, StringComparison.Ordinal);
        Assert.Contains("Quit", installer, StringComparison.Ordinal);
        Assert.Contains("Please exit FlowCast, then run Setup again.", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedProduct_HasNoLyricsProjectsOrRuntimeHooks()
    {
        var root = FindRepositoryRoot();
        var retainedFiles = new[]
        {
            "AudioShare.sln",
            "README.md",
            "ThirdPartyNotices.txt",
            "src/AudioShare.App/App.xaml.cs",
            "src/AudioShare.App/AudioShare.App.csproj",
            "src/AudioShare.Windows/AudioShare.Windows.csproj",
            "scripts/publish-release.ps1",
            "installer/FlowCast.nsi",
            "tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj",
        };

        foreach (var relativePath in retainedFiles)
        {
            var content = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("Lyrics", content, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(Directory.Exists(Path.Combine(root, "src", "AudioShare.Lyrics")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "AudioShare.Lyrics.Contracts")));
        Assert.False(Directory.Exists(Path.Combine(root, "lyrics-glass")));

        var releaseDirectory = Path.Combine(root, "release");
        if (Directory.Exists(releaseDirectory))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(
                releaseDirectory,
                "*Lyrics*",
                SearchOption.AllDirectories));
        }
    }

    private static string FindRepositoryRoot()
    {
        var sourceDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        return Path.GetFullPath(Path.Combine(sourceDirectory, "..", ".."));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
