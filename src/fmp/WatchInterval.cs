using System.Globalization;
using System.Text.RegularExpressions;

namespace FindMyPlane;

// Refresh interval for --watch: "30s", "1m", "2m", "1m30s".
public static partial class WatchInterval
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(10);
    public const string Examples = "30s, 1m, 2m, 1m30s";

    [GeneratedRegex("^(?:(?<m>[0-9]{1,4})m)?(?:(?<s>[0-9]{1,5})s)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    // null when `arg` isn't an interval at all (so it can be a flight/date instead)
    public static TimeSpan? Parse(string arg)
    {
        var match = Pattern().Match(arg);
        if (arg.Length == 0 || !match.Success) return null;
        var minutes = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = match.Groups["s"].Success ? int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    public static string Format(TimeSpan t)
    {
        var minutes = (int)t.TotalMinutes;
        return minutes == 0 ? $"{t.Seconds} s"
            : t.Seconds == 0 ? $"{minutes} min"
            : $"{minutes} min {t.Seconds} s";
    }
}
