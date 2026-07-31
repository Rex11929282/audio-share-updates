namespace AudioShare.Windows;

public sealed record VoicemeeterBananaEndpoints(
    ExternalAudioDevice Input,
    ExternalAudioDevice AuxInput);

public sealed record VoicemeeterBananaEndpointHealth(bool HasInput, bool HasAux);

public static class VoicemeeterBananaDetector
{
    public static VoicemeeterBananaEndpointHealth GetEndpointHealth(IEnumerable<ExternalAudioDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return new VoicemeeterBananaEndpointHealth(
            ExternalAudioDeviceSelector.FindMatches(devices, "Voicemeeter Input").Count == 1,
            ExternalAudioDeviceSelector.FindMatches(devices, "Voicemeeter AUX Input").Count == 1);
    }

    public static bool TryFindEndpoints(
        IEnumerable<ExternalAudioDevice> devices,
        out VoicemeeterBananaEndpoints? endpoints)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var inputMatches = ExternalAudioDeviceSelector.FindMatches(devices, "Voicemeeter Input");
        var auxMatches = ExternalAudioDeviceSelector.FindMatches(devices, "Voicemeeter AUX Input");
        if (inputMatches.Count != 1 || auxMatches.Count != 1)
        {
            endpoints = null;
            return false;
        }

        endpoints = new VoicemeeterBananaEndpoints(inputMatches[0], auxMatches[0]);
        return true;
    }
}
