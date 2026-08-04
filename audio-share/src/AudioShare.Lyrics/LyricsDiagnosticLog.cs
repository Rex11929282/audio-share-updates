using System.IO;

namespace AudioShare.Lyrics;

internal sealed class LyricsDiagnosticLog
{
    private readonly string path;

    internal LyricsDiagnosticLog(string path) => this.path = path;

    internal static LyricsDiagnosticLog CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics");
        return new LyricsDiagnosticLog(Path.Combine(directory, "diagnostics.log"));
    }

    internal void WriteTunerInitializationFailure(string stage, Exception exception)
    {
        WriteFailure("tuner-webview", stage, exception);
    }

    internal void WriteRendererFailure(string stage, Exception exception)
    {
        WriteFailure("renderer", stage, exception);
    }

    internal void WriteHostShutdownFailure(string stage, Exception exception)
    {
        WriteFailure("host-shutdown", stage, exception);
    }

    private void WriteFailure(string source, string stage, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"{DateTimeOffset.UtcNow:O} {source} stage={stage} {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
