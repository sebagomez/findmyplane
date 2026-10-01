using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindMyPlane;

public static class Ansi
{
    public static readonly bool Enabled = IsTerminal(Console.IsOutputRedirected);
    public static readonly bool ErrorEnabled = IsTerminal(Console.IsErrorRedirected);

    static bool IsTerminal(bool redirected) =>
        !redirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
        && Environment.GetEnvironmentVariable("TERM") != "dumb";

    public static string Style(string codes, string s) => Enabled ? $"\e[{codes}m{s}\e[0m" : s;
    public static string Bold(string s) => Style("1", s);
    public static string Dim(string s) => Style("2", s);
}

public static partial class Render
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Indent = "        "; // lines up under the airport code
    const int BarWidth = 24;

    // badge colors per raw status, mirroring the site's status-* classes
    static readonly Dictionary<string, string> StatusColors = new()
    {
        ["enroute"] = "1;97;44", ["departed"] = "1;97;44", ["approaching"] = "1;97;44",
        ["arrived"] = "1;97;42", ["expected"] = "1;97;42", ["checkin"] = "1;97;42",
        ["boarding"] = "1;97;42", ["gateclosed"] = "1;97;42",
        ["delayed"] = "1;30;43",
        ["canceled"] = "1;97;41", ["canceleduncertain"] = "1;97;41",
        ["diverted"] = "1;97;45",
    };

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex CamelBoundary();

    public static string Flight(FlightLeg leg, DateOnly searchDate, Position? position)
    {
        var sb = new StringBuilder();
        var airline = leg.Airline?.Name is { Length: > 0 } name ? " " + Ansi.Dim("· " + name) : "";
        sb.AppendLine($"{Ansi.Bold(leg.Number ?? "")}{airline}  {Badge(leg)}");
        Side(sb, "From", leg.Departure, searchDate);
        Side(sb, "To", leg.Arrival, searchDate);
        if (leg.IsAirborne)
        {
            sb.Append("  Now   ");
            if (position is null)
                sb.AppendLine(Ansi.Dim("Position unavailable (no live signal, not enough data to estimate)."));
            else
                sb.AppendLine(Bar(leg, position)).AppendLine(Indent + Details(position));
        }

        var meta = new List<string>();
        if (leg.Aircraft?.Model is { Length: > 0 } model) meta.Add("✈ " + model);
        if (leg.Aircraft?.Reg is { Length: > 0 } reg) meta.Add("Reg " + reg);
        if (leg.CallSign is { Length: > 0 } callSign) meta.Add("Callsign " + callSign);
        if (leg.GreatCircleDistance?.Km is { } km) meta.Add(Math.Round(km).ToString("N0", Inv) + " km");
        if (meta.Count > 0) sb.AppendLine("  " + string.Join(" · ", meta));
        return sb.ToString();
    }

    public static string WatchLine(FlightLeg leg, Position? position, DateTime now) =>
        $"  {Ansi.Dim(now.ToString("HH:mm:ss", Inv))}  " +
        (position is null ? Ansi.Dim("Position unavailable.") : $"{Bar(leg, position)}  {Details(position)}");

    static string StatusLabel(FlightLeg leg)
    {
        var s = leg.Status ?? "Unknown";
        // "Expected" reads better as "On time" when there is no delay
        if (s == "Expected" && (leg.Departure?.MinutesLate ?? 0) <= 0) s = "On time";
        return CamelBoundary().Replace(s, "$1 $2");
    }

    static string Badge(FlightLeg leg)
    {
        var label = StatusLabel(leg);
        if (!Ansi.Enabled) return $"[{label}]";
        return StatusColors.TryGetValue((leg.Status ?? "").ToLowerInvariant(), out var color)
            ? Ansi.Style(color, $" {label.ToUpperInvariant()} ")
            : Ansi.Bold(label);
    }

    static void Side(StringBuilder sb, string label, FlightSide? side, DateOnly searchDate)
    {
        sb.Append($"  {label,-4}  {Ansi.Bold(side?.Airport?.Code ?? "???")}");
        sb.AppendLine(side?.Airport?.Name is { Length: > 0 } name ? "  " + name : "");
        if (side is null) return;
        if (DayNote(side, searchDate) is { } note) sb.AppendLine(Indent + Ansi.Style("36", note));
        sb.AppendLine(Indent + TimeLine(side));
        var where = new List<string>();
        if (side.Terminal is { Length: > 0 } terminal) where.Add("Terminal " + terminal);
        if (side.Gate is { Length: > 0 } gate) where.Add("Gate " + gate);
        if (where.Count > 0) sb.AppendLine(Indent + Ansi.Dim(string.Join(" · ", where)));
    }

    // wall-clock time at the airport ("2024-05-12 10:30+02:00" -> "10:30")
    static string Clock(string? local) => AdbTime.Parse(local)?.ToString("HH:mm", Inv) ?? "—";

    static string TimeLine(FlightSide side)
    {
        var scheduled = Clock(side.ScheduledTime?.Local);
        var best = side.BestLocal is null ? null : Clock(side.BestLocal);
        var line = best is not null && best != scheduled
            ? $"{Ansi.Style("2;9", scheduled)} → {Ansi.Bold(best)}"
            : Ansi.Bold(scheduled);
        var late = side.MinutesLate;
        if (late > 3) line += "  " + Ansi.Style("33", $"+{late} min");
        else if (late < -3) line += "  " + Ansi.Style("32", $"{late} min");
        return line;
    }

    // notice when a leg's local date differs from the searched date, e.g. "📅 Sep 29 · day before"
    static string? DayNote(FlightSide side, DateOnly searchDate)
    {
        var local = side.ScheduledTime?.Local;
        if (local is not { Length: >= 10 }
            || !DateOnly.TryParseExact(local[..10], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day))
            return null;
        var diff = day.DayNumber - searchDate.DayNumber;
        if (diff == 0) return null;
        var relative = diff switch
        {
            -1 => "day before",
            1 => "day after",
            > 0 => $"+{diff} days",
            _ => $"{diff} days",
        };
        return $"📅 {day.ToString("MMM d", Inv)} · {relative}";
    }

    // SFO ━━━━━━━━━━✈────────────── SIN  42%
    static string Bar(FlightLeg leg, Position position)
    {
        var from = Ansi.Bold(leg.Departure?.Airport?.Code ?? "???");
        var to = Ansi.Bold(leg.Arrival?.Airport?.Code ?? "???");
        if (position.Progress is not { } f) return $"{from} → {to}";
        var flown = (int)Math.Round(f * (BarWidth - 1));
        var plane = position.Estimated ? Ansi.Dim("✈") : Ansi.Style("1;34", "✈");
        return $"{from} {Ansi.Style("34", new string('━', flown))}{plane}{Ansi.Dim(new string('─', BarWidth - 1 - flown))} {to}  {Math.Round(f * 100)}%";
    }

    static string Details(Position p)
    {
        var bits = new List<string>();
        if (p.AltitudeFt is { } alt) bits.Add(alt <= 0 ? "on ground" : Math.Round(alt).ToString("N0", Inv) + " ft");
        if (p.SpeedKt is { } gs) bits.Add($"{Math.Round(gs)} kt");
        if (p.Track is { } track) bits.Add($"hdg {Math.Round(track) % 360}°");
        bits.Add(string.Create(Inv, $"{Math.Abs(p.Lat):0.00}°{(p.Lat >= 0 ? 'N' : 'S')} {Math.Abs(p.Lon):0.00}°{(p.Lon >= 0 ? 'E' : 'W')}"));
        bits.Add(p.Estimated ? "≋ estimated by elapsed time (outside ADS-B coverage)" : "via " + p.Source);
        return Ansi.Dim(string.Join(" · ", bits));
    }
}
