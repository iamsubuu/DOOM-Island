using System.Text.RegularExpressions;
using NOVAIsland.Core.Enums;

namespace NOVAIsland.Core.Services;

public static class TimeFormatter
{
    public static string FormatTime(double seconds)
    {
        var safe = Math.Max(0, (int)Math.Floor(seconds));
        var mins = safe / 60;
        var secs = safe % 60;
        return $"{mins}:{secs:D2}";
    }

    public static string FormatStopwatch(long tenthsOfSecond)
    {
        var safe = Math.Max(0, tenthsOfSecond);
        var totalSeconds = safe / 10;
        var mins = totalSeconds / 60;
        var secs = totalSeconds % 60;
        var tenths = safe % 10;
        return $"{mins:D2}:{secs:D2}.{tenths}";
    }
}

public static partial class SensitiveContentDetector
{
    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"^(?:ghp|gho|github_pat|sk_live|sk_test|AIza|xox[baprs]-)[A-Za-z0-9_-]{12,}$", RegexOptions.Compiled)]
    private static partial Regex ApiTokenRegex();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    public static bool IsExcludedApp(string? processName, IReadOnlyCollection<string> excludedApps)
    {
        if (string.IsNullOrWhiteSpace(processName) || excludedApps.Count == 0)
        {
            return false;
        }

        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";

        return excludedApps.Any(app =>
            string.Equals(app, processName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(app, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksSensitive(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.Trim();
        return JwtRegex().IsMatch(trimmed) ||
               ApiTokenRegex().IsMatch(trimmed) ||
               PrivateKeyRegex().IsMatch(trimmed);
    }

    public static ClipboardItemType ClassifyText(string content)
    {
        var trimmed = content.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return ClipboardItemType.Link;
        }

        if (trimmed.Contains('\n') &&
            (trimmed.Contains('{') || trimmed.Contains("=>") || trimmed.Contains("public ") ||
             trimmed.Contains("function ") || trimmed.Contains("const ") || trimmed.Contains("SELECT ")))
        {
            return ClipboardItemType.Code;
        }

        return ClipboardItemType.Text;
    }
}

public readonly record struct SpringConfig(float DampingRatio, TimeSpan Period, bool Instant);

public static class SpringPhysicsConfigMapper
{
    public static SpringConfig FromPreset(AnimationPreset preset, double speedMultiplier, bool reduceMotion)
    {
        if (reduceMotion || preset == AnimationPreset.Instant)
        {
            return new SpringConfig(1.0f, TimeSpan.FromMilliseconds(40), Instant: true);
        }

        var clampedSpeed = Math.Clamp(speedMultiplier, 0.5, 1.8);
        var (damping, basePeriodMs) = preset switch
        {
            AnimationPreset.Snappy => (0.82f, 210.0),
            AnimationPreset.Silk => (0.76f, 340.0),
            _ => (0.78f, 265.0) // Fluid
        };

        return new SpringConfig(damping, TimeSpan.FromMilliseconds(basePeriodMs / clampedSpeed), Instant: false);
    }
}
