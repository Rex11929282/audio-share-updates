using System.Collections.Immutable;

namespace AudioShare.Core;

public sealed record FlowCastPreferences
{
    public IReadOnlySet<string> ExcludedProcesses { get; }

    public bool ReduceMotion { get; init; }

    public int StartCountdownSeconds { get; init; } = 3;

    public bool RestoreLocalPlayback { get; init; } = true;

    public bool EndSharingSoundEnabled { get; init; } = true;

    public static FlowCastPreferences Empty { get; } = new(
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase),
        reduceMotion: false);

    public FlowCastPreferences(
        IEnumerable<string>? excludedProcesses,
        bool reduceMotion,
        int startCountdownSeconds = 3,
        bool restoreLocalPlayback = true,
        bool endSharingSoundEnabled = true)
    {
        ExcludedProcesses = (excludedProcesses ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        ReduceMotion = reduceMotion;
        StartCountdownSeconds = startCountdownSeconds is 0 or 3 or 5 ? startCountdownSeconds : 3;
        RestoreLocalPlayback = restoreLocalPlayback;
        EndSharingSoundEnabled = endSharingSoundEnabled;
    }

    public FlowCastPreferences Exclude(string processName) =>
        new(ExcludedProcesses.Append(Normalize(processName)).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), ReduceMotion, StartCountdownSeconds, RestoreLocalPlayback, EndSharingSoundEnabled);

    public FlowCastPreferences Restore(string processName) =>
        new(ExcludedProcesses.Where(name => !string.Equals(name, Normalize(processName), StringComparison.OrdinalIgnoreCase))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), ReduceMotion, StartCountdownSeconds, RestoreLocalPlayback, EndSharingSoundEnabled);

    public bool IsExcluded(string processName) => ExcludedProcesses.Contains(Normalize(processName));

    private static string Normalize(string processName) => Path.GetFileNameWithoutExtension(processName).Trim();
}
