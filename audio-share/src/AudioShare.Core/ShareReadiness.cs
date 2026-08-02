namespace AudioShare.Core;

public sealed class FavoritePrograms
{
    private readonly HashSet<string> programNames;

    public FavoritePrograms(IEnumerable<string>? programNames = null)
    {
        this.programNames = programNames is null
            ? []
            : programNames.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Names => programNames;

    public bool Contains(string processName) => programNames.Contains(Normalize(processName));

    public bool Toggle(string processName)
    {
        var normalized = Normalize(processName);
        return programNames.Contains(normalized)
            ? !programNames.Remove(normalized)
            : programNames.Add(normalized);
    }

    public IReadOnlyList<AudioSession> Order(IEnumerable<AudioSession> sessions) =>
        sessions
            .OrderByDescending(session => Contains(session.ProcessName))
            .ThenBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string Normalize(string processName) =>
        Path.GetFileNameWithoutExtension(processName).Trim().ToUpperInvariant();
}

public static class ShareTimerPresets
{
    public static IReadOnlyList<int> Minutes { get; } = [5, 15, 30, 60];
}

public static class AudioActivityPolicy
{
    // Ignore persistent background noise from applications with an idle audio session.
    public const float MinimumPeakLevel = 0.02f;

    public static bool HasConfirmedOutput(float initialPeakLevel, float confirmedPeakLevel) =>
        initialPeakLevel >= MinimumPeakLevel && confirmedPeakLevel >= MinimumPeakLevel;

    public static bool HasConfirmedOutput(float initialPeakLevel, float confirmedPeakLevel, float sustainedPeakLevel) =>
        new[] { initialPeakLevel, confirmedPeakLevel, sustainedPeakLevel }
            .Count(level => level >= MinimumPeakLevel) >= 2;
}

public static class ShareStartPolicy
{
    public static bool CanStart(bool hasAudibleSelection, bool routeAvailable, bool isRoutingOperation) =>
        hasAudibleSelection && routeAvailable && !isRoutingOperation;
}
