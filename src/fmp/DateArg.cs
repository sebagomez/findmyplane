using System.Globalization;

namespace FindMyPlane;

public static class DateArg
{
    public const string Help = "today (default), tomorrow, yesterday, or YYYY-MM-DD";

    public static bool TryParse(string? arg, DateOnly today, out DateOnly date)
    {
        switch (arg?.Trim().ToLowerInvariant())
        {
            case null or "" or "today": date = today; return true;
            case "tomorrow": date = today.AddDays(1); return true;
            case "yesterday": date = today.AddDays(-1); return true;
        }
        return DateOnly.TryParseExact(arg.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
