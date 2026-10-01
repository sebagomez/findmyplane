using System.Globalization;
using System.Text.Json.Serialization;

namespace FindMyPlane;

// Subset of the AeroDataBox /flights/number response that fmp uses.
public sealed record FlightLeg(
    string? Number,
    string? CallSign,
    string? Status,
    Airline? Airline,
    FlightSide? Departure,
    FlightSide? Arrival,
    Aircraft? Aircraft,
    Distance? GreatCircleDistance,
    LegLocation? Location)
{
    static readonly string[] AirborneStatuses = ["enroute", "departed", "approaching"];
    static readonly string[] FinishedStatuses = ["arrived", "canceled", "diverted"];

    // "Is this flight flying right now": status only. A `location` is NOT proof —
    // AeroDataBox reports the aircraft's position even before this flight departs.
    public bool IsAirborne =>
        AirborneStatuses.Contains((Status ?? "").ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", ""));

    // Sort order: flying first, then upcoming, finished flights last.
    public int Rank =>
        IsAirborne ? 0
        : FinishedStatuses.Any(s => (Status ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)) ? 2
        : 1;
}

public sealed record FlightSide(
    Airport? Airport,
    FlightTime? ScheduledTime,
    FlightTime? RevisedTime,
    FlightTime? PredictedTime,
    FlightTime? RunwayTime,
    string? Terminal,
    string? Gate)
{
    // Best known actual/expected time, airport-local.
    public string? BestLocal => RunwayTime?.Local ?? RevisedTime?.Local ?? PredictedTime?.Local;

    public int MinutesLate
    {
        get
        {
            var sched = AdbTime.Parse(ScheduledTime?.Local);
            var actual = AdbTime.Parse(BestLocal);
            return sched is null || actual is null ? 0 : (int)Math.Round((actual.Value - sched.Value).TotalMinutes);
        }
    }
}

public sealed record Airport(string? Iata, string? Icao, string? Name, GeoPoint? Location)
{
    public string Code => Iata ?? Icao ?? "???";
}

public sealed record GeoPoint(double Lat, double Lon);

public sealed record FlightTime(string? Utc, string? Local);

public sealed record Aircraft(string? Reg, string? Model);

public sealed record Airline(string? Name);

public sealed record Distance(double? Km);

public sealed record LegLocation(double? Lat, double? Lon, Measure? TrueTrack, Measure? Altitude, Measure? GroundSpeed);

public sealed record Measure(double? Deg, double? Feet, double? Kt);

public sealed record FmpConfig(string? RapidApiKey);

public static class AdbTime
{
    // AeroDataBox local times look like "2024-05-12 10:30+02:00".
    public static DateTimeOffset? Parse(string? t) =>
        t is not null && DateTimeOffset.TryParse(t.Replace(' ', 'T'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(FlightLeg[]))]
[JsonSerializable(typeof(FmpConfig))]
internal sealed partial class FmpJson : JsonSerializerContext;
