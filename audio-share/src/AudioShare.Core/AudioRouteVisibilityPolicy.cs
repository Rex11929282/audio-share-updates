namespace AudioShare.Core;

public static class AudioRouteVisibilityPolicy
{
    private static readonly string[] HiddenDevicePrefixes =
    [
        "Voicemeeter In 1",
        "Voicemeeter In 2",
        "Voicemeeter In 3",
        "Voicemeeter In 4",
        "Voicemeeter In 5",
        "Voicemeeter VAIO3 Input",
    ];

    public static bool IsHiddenNonSharingDevice(string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) &&
        HiddenDevicePrefixes.Any(prefix => deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
