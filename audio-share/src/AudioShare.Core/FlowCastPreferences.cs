using System.Collections.Immutable;

namespace AudioShare.Core;

public sealed record FlowCastPreferences
{
    public IReadOnlySet<string> ExcludedProcesses { get; }

    public bool ReduceMotion { get; init; }

    public bool RestoreLocalPlayback { get; init; } = true;

    public bool EndSharingSoundEnabled { get; init; } = true;

    public bool DisconnectNotificationsEnabled { get; init; } = true;

    public bool QuickStartCompleted { get; init; }

    public static FlowCastPreferences Empty { get; } = new(
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase),
        reduceMotion: false);

    public FlowCastPreferences(
        IEnumerable<string>? excludedProcesses,
        bool reduceMotion,
        bool restoreLocalPlayback = true,
        bool endSharingSoundEnabled = true,
        bool disconnectNotificationsEnabled = true,
        bool quickStartCompleted = false)
    {
        ExcludedProcesses = (excludedProcesses ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        ReduceMotion = reduceMotion;
        RestoreLocalPlayback = restoreLocalPlayback;
        EndSharingSoundEnabled = endSharingSoundEnabled;
        DisconnectNotificationsEnabled = disconnectNotificationsEnabled;
        QuickStartCompleted = quickStartCompleted;
    }

    public FlowCastPreferences Exclude(string processName) =>
        new(ExcludedProcesses.Append(Normalize(processName)).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), ReduceMotion, RestoreLocalPlayback, EndSharingSoundEnabled, DisconnectNotificationsEnabled, QuickStartCompleted);

    public FlowCastPreferences Restore(string processName) =>
        new(ExcludedProcesses.Where(name => !string.Equals(name, Normalize(processName), StringComparison.OrdinalIgnoreCase))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), ReduceMotion, RestoreLocalPlayback, EndSharingSoundEnabled, DisconnectNotificationsEnabled, QuickStartCompleted);

    public FlowCastPreferences CompleteQuickStart() =>
        new(ExcludedProcesses, ReduceMotion, RestoreLocalPlayback, EndSharingSoundEnabled, DisconnectNotificationsEnabled, quickStartCompleted: true);

    public bool IsExcluded(string processName) => ExcludedProcesses.Contains(Normalize(processName));

    private static string Normalize(string processName) => Path.GetFileNameWithoutExtension(processName).Trim();
}
