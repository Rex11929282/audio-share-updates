namespace AudioShare.Windows;

public sealed record RoutingHelperManifest(string HelperSha256, string RouterVersion);

public sealed record ExternalRoutingHealth(bool IsAvailable, string Message);

public sealed record ExternalAudioDevice(string Id, string Name);

public static class ExternalAudioDeviceSelector
{
    public static IReadOnlyList<ExternalAudioDevice> FindMatches(
        IEnumerable<ExternalAudioDevice> devices,
        string endpointLabel)
    {
        return devices.Where(device => IsMatch(device.Name, endpointLabel)).ToArray();
    }

    private static bool IsMatch(string name, string endpointLabel)
    {
        if (string.Equals(name, endpointLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!name.StartsWith(endpointLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var description = name.AsSpan(endpointLabel.Length).Trim();
        return description.Length >= 2 && description[0] == '(' && description[^1] == ')';
    }
}
