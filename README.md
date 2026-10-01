# findmyplane
The legendary app, now in your cli

`fmp` takes a flight number and a date and shows the flight's status, times, gates, and aircraft. If the flight is in the air, it also shows where the plane is right now.

`fmp` takes a flight number and a date and shows ...  

<img src="res/Flight%20CX%20828.png" alt="fmp CX828 output" width="500">

## A bit of history

`fmp` is the latest version of an old idea. In 2012 I built [Find my Plane](https://sgomez.blogspot.com/2012/06/find-my-plane.html), a smartphone app (Android and iOS) for GeneXus's internal "GX Challenge (Developer Edition)" contest. You entered a flight number and it showed the route, terminal and gate, and whether the flight was on time, delayed, or diverted. It also showed the weather at both airports and let you listen to their air traffic controllers.

To promote it, I then built the [@FindMyPlane Twitter bot](https://sgomez.blogspot.com/2012/06/findmyplane-bot.html). You tweeted a flight number at @FindMyPlane and it replied with the same kind of info, squeezed into 140 characters. It ran on Windows Azure worker roles.

## Usage

```
fmp <flight> [date] [--watch [interval]]
fmp --set-key <rapidapi-key>
fmp --clear-key
```

- `flight`: a flight number, e.g. `UA1`, `lx18`, or `"AA 705"`. Case and spaces don't matter.
- `date`: `today` (the default), `tomorrow`, `yesterday`, or `YYYY-MM-DD`. Relative dates use your local date.
- `-w`, `--watch [interval]`: refreshes the position of an airborne flight, every 30 s by default. The interval can be `30s`, `1m`, `2m`, `1m30s`, etc. (minimum 10 s). Refreshes only call adsb.lol, so they don't use your AeroDataBox quota. Stop it with Ctrl+C.

Results are sorted with flights in the air first, then upcoming ones, then finished ones (arrived, canceled, diverted). A 📅 note appears when a departure or arrival falls on a different local date than the one you searched.

Exit codes: `0` success, `1` no flights found or an API/network error, `2` usage error.

## Data sources

- **Status, times, aircraft:** [AeroDataBox on RapidAPI](https://rapidapi.com/aedbx-aedbx/api/aerodatabox) (free tier available; it allows about one request per second).
- **Live position:** [adsb.lol](https://adsb.lol), looked up by registration and callsign. No key needed. If there's no ADS-B signal, `fmp` uses AeroDataBox's position. If that's missing too, it estimates the position along the great-circle route from elapsed flight time (marked `≋ estimated`).

## API key

`fmp` reads the RapidAPI key from `$FMP_RAPIDAPI_KEY`. If that isn't set, it uses the key saved with `fmp --set-key <key>`, which is stored in `~/.config/fmp/config.json` (`$XDG_CONFIG_HOME/fmp`, or `%APPDATA%\fmp` on Windows) with mode `600`.

## Build & run

Requires the .NET 10 SDK to build. Run from source:

```sh
dotnet run --project src/fmp -- UA1 tomorrow
```

### Single-file executable

Publish one executable for your platform's [runtime identifier](https://learn.microsoft.com/dotnet/core/rid-catalog): `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`. You can publish for any of them from any OS.

```sh
dotnet publish src/fmp -c Release -r osx-arm64
# -> src/fmp/bin/Release/net10.0/osx-arm64/publish/fmp   (fmp.exe on win-*)
```

Copy that file anywhere on your `PATH` and run it:

```sh
fmp UA1 yesterday
```

The executable contains `fmp` and its dependencies, but **not** the .NET runtime, so the machine running it needs the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed.

### Bundling the .NET runtime

To make the executable run on machines without .NET, include the runtime. The file grows from about 300 KB to about 75–80 MB. Either set it in `src/fmp/fmp.csproj`:

```xml
<SelfContained>true</SelfContained>
```

or override it for a single publish:

```sh
dotnet publish src/fmp -c Release -r osx-arm64 --self-contained
```
