using System.Text.Json;

namespace FindMyPlane;

// API key: FMP_RAPIDAPI_KEY env var wins, else the key saved with `fmp --set-key`.
public static class Config
{
    public const string EnvVar = "FMP_RAPIDAPI_KEY";

    public static string FilePath { get; } = Path.Combine(ConfigDir(), "fmp", "config.json");

    // %APPDATA% on Windows; $XDG_CONFIG_HOME or ~/.config elsewhere (macOS included)
    static string ConfigDir() =>
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify), ".config");

    public static string? GetApiKey()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar)?.Trim();
        if (!string.IsNullOrEmpty(env)) return env;
        if (!File.Exists(FilePath)) return null;
        var saved = JsonSerializer.Deserialize(File.ReadAllText(FilePath), FmpJson.Default.FmpConfig)?.RapidApiKey?.Trim();
        return string.IsNullOrEmpty(saved) ? null : saved;
    }

    public static void SaveApiKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(FilePath, options);
        JsonSerializer.Serialize(stream, new FmpConfig(key), FmpJson.Default.FmpConfig);
    }

    public static bool ClearApiKey()
    {
        if (!File.Exists(FilePath)) return false;
        File.Delete(FilePath);
        return true;
    }
}
