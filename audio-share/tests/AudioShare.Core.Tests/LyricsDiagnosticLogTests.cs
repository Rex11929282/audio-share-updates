using System.IO;

namespace AudioShare.Core.Tests;

public sealed class LyricsDiagnosticLogTests
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-diagnostics-{Guid.NewGuid():N}");

    [Fact]
    public void WriteTunerInitializationFailure_AppendsStageAndExceptionToTheConfiguredFile()
    {
        var path = Path.Combine(directory, "diagnostics.log");
        var log = new global::AudioShare.Lyrics.LyricsDiagnosticLog(path);

        log.WriteTunerInitializationFailure("mapping", new InvalidOperationException("expected test failure"));

        var text = File.ReadAllText(path);
        Assert.Contains("tuner-webview", text);
        Assert.Contains("stage=mapping", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("expected test failure", text);
    }
}
