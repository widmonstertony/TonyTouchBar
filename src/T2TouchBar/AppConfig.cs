using System.Text.Json;
using System.Text.Json.Serialization;

namespace T2TouchBar;

internal sealed class AppConfig
{
    public string WslDistribution { get; set; } = "Ubuntu-24.04";
    public string BridgePath { get; set; } = "/opt/t2touchbar/t2-touchbar-bridge";
    public int RefreshMilliseconds { get; set; } = 250;
    public int ForzaTelemetryPort { get; set; } = 5607;
    public List<string> GamePathMarkers { get; set; } = ["\\XboxGames\\", "\\steamapps\\common\\", "\\Epic Games\\", "\\GOG Games\\"];
    public List<AppProfile> Profiles { get; set; } = [];

    public static AppConfig Load()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyTouchBar", "settings.json");
        var local = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var path = File.Exists(installed) ? installed : local;
        if (!File.Exists(path)) return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new AppConfig();
    }

    public AppProfile? ResolveProfile(ForegroundApp app)
    {
        var configured = Profiles.FirstOrDefault(item => item.Executable.Equals(app.Executable, StringComparison.OrdinalIgnoreCase));
        if (configured is not null) return configured;
        var likelyGame = app.Executable.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ||
                         (!string.IsNullOrWhiteSpace(app.Path) && GamePathMarkers.Any(marker => app.Path.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (!likelyGame) return null;
        var title = string.IsNullOrWhiteSpace(app.Title) ? Path.GetFileNameWithoutExtension(app.Executable) : app.Title;
        return new AppProfile
        {
            Executable = app.Executable,
            Title = title,
            Accent = "#3558A8",
            Shortcuts =
            [
                new ShortcutConfig { Label = "Screenshot", Key = "F12" },
                new ShortcutConfig { Label = "Pause", Key = "ESCAPE" }
            ]
        };
    }
}

internal sealed class AppProfile
{
    public string Executable { get; set; } = "";
    public string Title { get; set; } = "";
    public string Accent { get; set; } = "#5B2A86";
    public string Dashboard { get; set; } = "";
    public List<ShortcutConfig> Shortcuts { get; set; } = [];

    [JsonIgnore]
    public bool UsesForzaDashboard => string.Equals(Dashboard, "forza", StringComparison.OrdinalIgnoreCase) ||
        Executable.Contains("ForzaHorizon", StringComparison.OrdinalIgnoreCase) ||
        Executable.Equals("forza_gaming.desktop.x64_release_final.exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed class ShortcutConfig
{
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
}
