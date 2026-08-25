using AudioShare.Core;

namespace AudioShare.App;

public static class RouteDisplayFormatter
{
    public static string Summarize(AudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return SummarizeDevice(session.OutputDeviceName);
    }

    public static string SummarizeDevice(string? outputDeviceName)
    {
        if (string.IsNullOrWhiteSpace(outputDeviceName))
        {
            return "跟隨 Windows 默認播放設備";
        }

        var names = outputDeviceName
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length > 1)
        {
            return "多個播放設備";
        }

        return names[0].Length > 34
            ? $"{names[0][..31]}..."
            : names[0];
    }
}
