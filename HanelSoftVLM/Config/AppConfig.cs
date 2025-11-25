using System.Text.Json;

namespace HanelSoftVLM.Config;

// Maps to appsettings.json - properties are auto-populated when deserialized
public class AppConfig
{
    public int ProcessingIntervalSeconds { get; set; } = 60;
    public string ApiBaseUrl { get; set; } = "";
    public EndpointsConfig Endpoints { get; set; } = new();

    public static AppConfig Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }
}

// API endpoint paths appended to ApiBaseUrl
public class EndpointsConfig
{
    public string CommissionSave { get; set; } = "";
    public string CommissionRelease { get; set; } = "";
    public string CommissionExists { get; set; } = "";
    public string ItemDefinitionFind { get; set; } = "";
    public string ItemDefinitionSave { get; set; } = "";
}
