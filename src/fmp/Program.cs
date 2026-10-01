using System.Globalization;
using System.Reflection;

namespace FindMyPlane;

public static class Program
{
    static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    static string Help => $"""
        fmp — find my plane

        Usage:
          fmp <flight> [date] [--watch [interval]]
          fmp --set-key <rapidapi-key>
          fmp --clear-key

        Arguments:
          flight         Flight number, e.g. UA1, LX18, AV9
          date           {DateArg.Help}

        Options:
          -w, --watch [interval]
                         Keep refreshing the position of an airborne flight,
                         every {WatchInterval.Format(WatchInterval.Default)} by default (interval: {WatchInterval.Examples})
          --set-key      Save your AeroDataBox RapidAPI key to {Config.FilePath}
          --clear-key    Remove the saved key
          -h, --help     Show this help
          --version      Show the version

        The API key is read from ${Config.EnvVar}, falling back to the saved key.
        Get one (free tier available): https://rapidapi.com/aedbx-aedbx/api/aerodatabox

        """;

    public static async Task<int> Main(string[] args)
    {
        var positional = new List<string>();
        TimeSpan? watch = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.Write(Help);
                    return 0;
                case "--version":
                    Console.WriteLine(Version);
                    return 0;
                case "-w" or "--watch":
                    watch = WatchInterval.Default;
                    if (i + 1 < args.Length && WatchInterval.Parse(args[i + 1]) is { } interval)
                    {
                        if (interval < WatchInterval.Minimum)
                            return Usage($"Watch interval must be at least {WatchInterval.Format(WatchInterval.Minimum)}.");
                        watch = interval;
                        i++;
                    }
                    break;
                case "--set-key":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1])) return Usage("--set-key needs a key.");
                    return SetKey(args[i + 1].Trim());
                case "--clear-key":
                    Console.WriteLine(Config.ClearApiKey() ? $"Removed saved key ({Config.FilePath})." : "No saved key.");
                    return 0;
                case { Length: > 1 } option when option[0] == '-':
                    return Usage($"Unknown option '{option}'.");
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count == 0)
        {
            Console.Error.Write(Help);
            return 2;
        }
        if (positional.Count > 2) return Usage("Too many arguments.");

        var flight = string.Concat(positional[0].Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (flight.Length == 0) return Usage("Missing flight number.");
        var dateArg = positional.ElementAtOrDefault(1);
        if (!DateArg.TryParse(dateArg, DateOnly.FromDateTime(DateTime.Now), out var date))
            return Usage($"Invalid date '{dateArg}' — use {DateArg.Help}.");

        if (Config.GetApiKey() is not { } apiKey)
        {
            Error($"No RapidAPI key. Set ${Config.EnvVar} or run: fmp --set-key <key>\n" +
                  "Get one (free tier available): https://rapidapi.com/aedbx-aedbx/api/aerodatabox");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"fmp/{Version}");

        try
        {
            return await RunAsync(http, apiKey, flight, date, watch, cts.Token);
        }
        catch (FmpException e)
        {
            Error(e.Message);
            return 1;
        }
        catch (HttpRequestException e)
        {
            Error($"Network error: {e.Message}");
            return 1;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return 130;
        }
        catch (TaskCanceledException)
        {
            Error("Request timed out.");
            return 1;
        }
    }

    static async Task<int> RunAsync(HttpClient http, string apiKey, string flight, DateOnly date, TimeSpan? watch, CancellationToken ct)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FlightLeg[] legs;
        Position?[] positions;
        FlightLeg[] airborne;
        try
        {
            Progress($"Looking up {flight} on {day}…");
            legs = (await AeroDataBox.GetFlightAsync(http, apiKey, flight, date, ct)).OrderBy(l => l.Rank).ToArray();
            airborne = legs.Where(l => l.IsAirborne).ToArray();
            if (airborne.Length > 0) Progress("Locating the plane…");
            positions = await Task.WhenAll(airborne.Select(l => Live.LocateAsync(http, l, ct)));
        }
        finally
        {
            Progress(null);
        }

        if (legs.Length == 0)
        {
            Error($"No flights found for {flight} on {day}.");
            return 1;
        }
        if (legs.Length > 1)
            Console.WriteLine(Ansi.Dim($"{legs.Length} flights found (departure or arrival on {day})." + Environment.NewLine));
        foreach (var leg in legs)
        {
            var i = Array.IndexOf(airborne, leg);
            Console.WriteLine(Render.Flight(leg, date, i >= 0 ? positions[i] : null));
        }

        if (watch is not { } interval) return 0;
        if (airborne.Length == 0)
        {
            Console.WriteLine(Ansi.Dim("Nothing in the air right now — nothing to watch."));
            return 0;
        }
        var tracked = airborne[0];
        Console.WriteLine(Ansi.Dim($"Watching {tracked.Number} — refreshing every {WatchInterval.Format(interval)}, Ctrl+C to stop."));
        try
        {
            while (true)
            {
                await Task.Delay(interval, ct);
                Console.WriteLine(Render.WatchLine(tracked, await Live.LocateAsync(http, tracked, ct), DateTime.Now));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
    }

    static int SetKey(string key)
    {
        Config.SaveApiKey(key);
        Console.WriteLine($"Saved RapidAPI key (…{key[^Math.Min(4, key.Length)..]}) to {Config.FilePath}.");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Config.EnvVar)))
            Console.WriteLine($"Note: ${Config.EnvVar} is set and takes precedence over the saved key.");
        return 0;
    }

    // transient status on an interactive stderr; null clears it
    static void Progress(string? text)
    {
        if (Ansi.ErrorEnabled) Console.Error.Write(text is null ? "\r\e[K" : $"\r\e[K{text}");
    }

    static void Error(string message) =>
        Console.Error.WriteLine(Ansi.ErrorEnabled ? $"\e[31mfmp:\e[0m {message}" : $"fmp: {message}");

    static int Usage(string message)
    {
        Error($"{message}\nRun 'fmp --help' for usage.");
        return 2;
    }
}
