using System.Text.Json;

namespace FindMyPlane;

public sealed record Position(
    double Lat,
    double Lon,
    double? Track,
    double? AltitudeFt,
    double? SpeedKt,
    string Source,
    bool Estimated,
    double? Progress); // fraction of the route flown, 0..1

public static class Live
{
    // Best available position: adsb.lol live > AeroDataBox location > estimate by elapsed time.
    public static async Task<Position?> LocateAsync(HttpClient http, FlightLeg leg, CancellationToken ct)
    {
        var pos = await FetchAdsbAsync(http, leg.Aircraft?.Reg, leg.CallSign, ct) ?? FromAeroDataBox(leg);
        return pos is null ? Estimate(leg, DateTimeOffset.Now) : pos with { Progress = RouteProgress(leg, pos) };
    }

    static async Task<Position?> FetchAdsbAsync(HttpClient http, string? reg, string? callsign, CancellationToken ct)
    {
        var urls = new List<string>();
        if (!string.IsNullOrEmpty(reg)) urls.Add($"https://api.adsb.lol/v2/reg/{Uri.EscapeDataString(reg)}");
        if (!string.IsNullOrEmpty(callsign)) urls.Add($"https://api.adsb.lol/v2/callsign/{Uri.EscapeDataString(callsign)}");
        // query in parallel, use the first (in order) that has an aircraft
        var results = await Task.WhenAll(urls.Select(url => TryFetchAdsbAsync(http, url, ct)));
        return results.FirstOrDefault(p => p is not null);
    }

    static async Task<Position?> TryFetchAdsbAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            if (!doc.RootElement.TryGetProperty("ac", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                return null;
            var ac = list[0];
            if (Number(ac, "lat") is not { } lat || Number(ac, "lon") is not { } lon) return null;
            // alt_baro is a number, or the string "ground"
            var alt = ac.TryGetProperty("alt_baro", out var a) && a.ValueKind == JsonValueKind.String ? 0 : Number(ac, "alt_baro");
            return new Position(lat, lon, Number(ac, "track"), alt, Number(ac, "gs"), "adsb.lol", Estimated: false, Progress: null);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null; // live data is best-effort
        }
    }

    static double? Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static Position? FromAeroDataBox(FlightLeg leg) =>
        leg.Location is { Lat: { } lat, Lon: { } lon } loc
            ? new Position(lat, lon, loc.TrueTrack?.Deg, loc.Altitude?.Feet, loc.GroundSpeed?.Kt, "AeroDataBox", Estimated: false, Progress: null)
            : null;

    // Position along the great circle from elapsed flight time.
    public static Position? Estimate(FlightLeg leg, DateTimeOffset now)
    {
        if (leg.Departure?.Airport?.Location is not { } a || leg.Arrival?.Airport?.Location is not { } b) return null;
        var dep = leg.Departure;
        var arr = leg.Arrival;
        var t0 = AdbTime.Parse(dep.RunwayTime?.Local ?? dep.RevisedTime?.Local ?? dep.ScheduledTime?.Local);
        var t1 = AdbTime.Parse(arr.PredictedTime?.Local ?? arr.RevisedTime?.Local ?? arr.ScheduledTime?.Local);
        if (t0 is null || t1 is null || t1 <= t0) return null;
        var f = Math.Clamp((now - t0.Value) / (t1.Value - t0.Value), 0.01, 0.99);
        var p = Geo.PointAt(a, b, f);
        var ahead = Geo.PointAt(a, b, Math.Min(1, f + 0.01));
        return new Position(p.Lat, p.Lon, Geo.Bearing(p, ahead), null, null, "estimated", Estimated: true, Progress: f);
    }

    static double? RouteProgress(FlightLeg leg, Position pos)
    {
        if (leg.Departure?.Airport?.Location is not { } a || leg.Arrival?.Airport?.Location is not { } b) return null;
        var p = new GeoPoint(pos.Lat, pos.Lon);
        var flown = Geo.DistanceKm(a, p);
        var total = flown + Geo.DistanceKm(p, b);
        return total > 0 ? flown / total : null;
    }
}

public static class Geo
{
    const double EarthRadiusKm = 6371.0;

    static double Rad(double d) => d * Math.PI / 180;
    static double Deg(double r) => r * 180 / Math.PI;

    static double CentralAngle(GeoPoint a, GeoPoint b)
    {
        double φ1 = Rad(a.Lat), λ1 = Rad(a.Lon), φ2 = Rad(b.Lat), λ2 = Rad(b.Lon);
        return Math.Acos(Math.Clamp(Math.Sin(φ1) * Math.Sin(φ2) + Math.Cos(φ1) * Math.Cos(φ2) * Math.Cos(λ2 - λ1), -1, 1));
    }

    public static double DistanceKm(GeoPoint a, GeoPoint b) => CentralAngle(a, b) * EarthRadiusKm;

    // Point at fraction f of the great circle from a to b.
    public static GeoPoint PointAt(GeoPoint a, GeoPoint b, double f)
    {
        var δ = CentralAngle(a, b);
        if (δ == 0) return a;
        double φ1 = Rad(a.Lat), λ1 = Rad(a.Lon), φ2 = Rad(b.Lat), λ2 = Rad(b.Lon);
        var A = Math.Sin((1 - f) * δ) / Math.Sin(δ);
        var B = Math.Sin(f * δ) / Math.Sin(δ);
        var x = A * Math.Cos(φ1) * Math.Cos(λ1) + B * Math.Cos(φ2) * Math.Cos(λ2);
        var y = A * Math.Cos(φ1) * Math.Sin(λ1) + B * Math.Cos(φ2) * Math.Sin(λ2);
        var z = A * Math.Sin(φ1) + B * Math.Sin(φ2);
        return new GeoPoint(Deg(Math.Atan2(z, Math.Sqrt(x * x + y * y))), Deg(Math.Atan2(y, x)));
    }

    public static double Bearing(GeoPoint p1, GeoPoint p2)
    {
        double φ1 = Rad(p1.Lat), φ2 = Rad(p2.Lat), Δλ = Rad(p2.Lon - p1.Lon);
        var θ = Math.Atan2(Math.Sin(Δλ) * Math.Cos(φ2), Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ));
        return (Deg(θ) + 360) % 360;
    }
}
