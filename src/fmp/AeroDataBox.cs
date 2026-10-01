using System.Net;
using System.Text.Json;

namespace FindMyPlane;

public sealed class FmpException(string message) : Exception(message);

public static class AeroDataBox
{
    const string Host = "aerodatabox.p.rapidapi.com";

    public static async Task<FlightLeg[]> GetFlightAsync(HttpClient http, string apiKey, string flight, DateOnly date, CancellationToken ct)
    {
        var url = $"https://{Host}/flights/number/{Uri.EscapeDataString(flight)}/{date:yyyy-MM-dd}?withLocation=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-RapidAPI-Key", apiKey);
        request.Headers.Add("X-RapidAPI-Host", Host);

        using var response = await http.SendAsync(request, ct);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound or HttpStatusCode.NoContent:
                return [];
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new FmpException("API key rejected — check your RapidAPI key/subscription.");
            case HttpStatusCode.TooManyRequests:
                throw new FmpException("Rate limit reached on the free AeroDataBox tier.");
        }
        if (!response.IsSuccessStatusCode)
            throw new FmpException($"AeroDataBox error (HTTP {(int)response.StatusCode}).");

        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (body.Length == 0) return [];
        try
        {
            if (body[0] == '[') return JsonSerializer.Deserialize(body, FmpJson.Default.FlightLegArray) ?? [];
            using var _ = JsonDocument.Parse(body); // valid JSON but not a list: no flights
            return [];
        }
        catch (JsonException)
        {
            throw new FmpException("AeroDataBox returned an unreadable response — try again.");
        }
    }
}
