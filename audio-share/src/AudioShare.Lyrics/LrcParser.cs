using System.Globalization;
using System.Text.RegularExpressions;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Lyrics;

public static partial class LrcParser
{
    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return [];
        }

        var parsedLines = new List<(long StartTimeMilliseconds, string Text)>();
        foreach (var row in lrc.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var matches = TimestampRegex().Matches(row);
            if (matches.Count == 0)
            {
                continue;
            }

            var finalTimestamp = matches[^1];
            var text = row[(finalTimestamp.Index + finalTimestamp.Length)..].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                parsedLines.Add((ParseTimestamp(match), text));
            }
        }

        var ordered = parsedLines.OrderBy(line => line.StartTimeMilliseconds).ToArray();
        var result = new LyricLine[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var current = ordered[index];
            long? endTime = null;
            for (var nextIndex = index + 1; nextIndex < ordered.Length; nextIndex++)
            {
                if (ordered[nextIndex].StartTimeMilliseconds > current.StartTimeMilliseconds)
                {
                    endTime = ordered[nextIndex].StartTimeMilliseconds;
                    break;
                }
            }

            result[index] = new LyricLine(current.Text, current.StartTimeMilliseconds, endTime);
        }

        return result;
    }

    private static long ParseTimestamp(Match match)
    {
        var minutes = long.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        var seconds = long.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["fraction"].Value;
        var milliseconds = fraction.Length == 0
            ? 0
            : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return ((minutes * 60) + seconds) * 1000 + milliseconds;
    }

    [GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]")]
    private static partial Regex TimestampRegex();
}
