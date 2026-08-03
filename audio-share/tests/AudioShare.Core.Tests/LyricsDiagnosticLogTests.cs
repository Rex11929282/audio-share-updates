using System.IO;

namespace AudioShare.Core.Tests;

public sealed class LyricsDiagnosticLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void WriteTunerInitializationFailure_AppendsEveryRecordToTheConfiguredFile()
    {
        var path = Path.Combine(directory, "diagnostics.log");
        var log = new global::AudioShare.Lyrics.LyricsDiagnosticLog(path);

        log.WriteTunerInitializationFailure("mapping", new InvalidOperationException("first expected test failure"));
        log.WriteTunerInitializationFailure("navigation", new InvalidOperationException("second expected test failure"));

        var text = File.ReadAllText(path);
        Assert.Contains("tuner-webview", text);
        Assert.Contains("stage=mapping", text);
        Assert.Contains("stage=navigation", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("first expected test failure", text);
        Assert.Contains("second expected test failure", text);
    }

    [Fact]
    public void WriteTunerInitializationFailure_SwallowsAnIoFailureFromItsConfiguredPath()
    {
        Directory.CreateDirectory(directory);
        var parentFile = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(parentFile, "occupied");
        var log = new global::AudioShare.Lyrics.LyricsDiagnosticLog(Path.Combine(parentFile, "diagnostics.log"));

        var exception = Record.Exception(
            () => log.WriteTunerInitializationFailure("setup", new InvalidOperationException("expected test failure")));

        Assert.Null(exception);
    }

    [Fact]
    public void WriteRendererFailure_AppendsTheRendererStageAndException()
    {
        var path = Path.Combine(directory, "diagnostics.log");
        var log = new global::AudioShare.Lyrics.LyricsDiagnosticLog(path);

        log.WriteRendererFailure("handshake", new InvalidOperationException("renderer failed"));

        var text = File.ReadAllText(path);
        Assert.Contains("renderer", text);
        Assert.Contains("stage=handshake", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("renderer failed", text);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
