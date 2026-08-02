namespace AudioShare.Core;

public sealed class ApplicationOrder
{
    private readonly List<string> processNames;

    public ApplicationOrder(IEnumerable<string>? processNames = null)
    {
        this.processNames = (processNames ?? [])
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> ProcessNames => processNames;

    public void Synchronize(IEnumerable<AudioSession> sessions)
    {
        foreach (var session in sessions)
        {
            var name = Normalize(session.ProcessName);
            if (!processNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                processNames.Add(name);
            }
        }
    }

    public IReadOnlyList<AudioSession> Order(IEnumerable<AudioSession> sessions)
    {
        var indexed = sessions.Select((session, index) => (Session: session, Index: index)).ToArray();
        var ranks = processNames
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        return indexed
            .OrderBy(item => ranks.TryGetValue(Normalize(item.Session.ProcessName), out var rank) ? rank : int.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Session)
            .ToArray();
    }

    public bool MoveBefore(AudioSession moved, AudioSession target)
    {
        var movedName = Normalize(moved.ProcessName);
        var targetName = Normalize(target.ProcessName);
        if (movedName.Length == 0 || targetName.Length == 0 ||
            string.Equals(movedName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        processNames.RemoveAll(name => string.Equals(name, movedName, StringComparison.OrdinalIgnoreCase));
        var targetIndex = processNames.FindIndex(name => string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            processNames.Add(targetName);
            targetIndex = processNames.Count - 1;
        }

        processNames.Insert(targetIndex, movedName);
        return true;
    }

    private static string Normalize(string processName) =>
        Path.GetFileNameWithoutExtension(processName).Trim().ToUpperInvariant();
}
