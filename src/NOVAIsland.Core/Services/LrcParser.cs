using System.Globalization;
using System.Text.RegularExpressions;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Core.Services;

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^\[(ar|ti|al|au|by|re|ve|length|offset):([^\]]*)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetadataTagRegex();

    public static IReadOnlyList<LyricLine> ParseSynced(string? lrcContent)
    {
        if (string.IsNullOrWhiteSpace(lrcContent))
        {
            return [];
        }

        var lines = lrcContent.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);
        double offsetSeconds = 0;
        var parsed = new List<LyricLine>();

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var metaMatch = MetadataTagRegex().Match(trimmed);
            if (metaMatch.Success)
            {
                if (string.Equals(metaMatch.Groups[1].Value, "offset", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(metaMatch.Groups[2].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetMs))
                {
                    offsetSeconds = offsetMs / 1000.0;
                }
                continue;
            }

            var matches = TimestampRegex().Matches(trimmed);
            if (matches.Count == 0)
            {
                continue;
            }

            var text = TimestampRegex().Replace(trimmed, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "♪";
            }

            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    continue;
                }

                double fraction = 0;
                if (match.Groups[3].Success)
                {
                    var fracStr = match.Groups[3].Value;
                    if (int.TryParse(fracStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fracVal))
                    {
                        fraction = fracStr.Length switch
                        {
                            1 => fracVal / 10.0,
                            2 => fracVal / 100.0,
                            _ => fracVal / 1000.0
                        };
                    }
                }

                var time = Math.Max(0, (minutes * 60) + seconds + fraction - offsetSeconds);
                parsed.Add(new LyricLine(Math.Round(time, 3), text));
            }
        }

        return parsed.OrderBy(l => l.TimeSeconds).ToList();
    }

    public static IReadOnlyList<LyricLine> ParsePlainFallback(string? plainLyrics)
    {
        if (string.IsNullOrWhiteSpace(plainLyrics))
        {
            return [];
        }

        return plainLyrics
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select((line, index) => new LyricLine(index * 4.0, line))
            .ToList();
    }

    public static int FindActiveLineIndex(IReadOnlyList<LyricLine> lines, double positionSeconds)
    {
        if (lines.Count == 0)
        {
            return -1;
        }

        var active = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TimeSeconds <= positionSeconds + 0.15)
            {
                active = i;
            }
            else
            {
                break;
            }
        }

        return active;
    }
}
