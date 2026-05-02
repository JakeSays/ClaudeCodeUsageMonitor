using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace ClaudeUsageMonitor.Services;

public class AppSettings
{
    public bool MinimizeToTray { get; set; }

    public bool ShowFiveHourGauge { get; set; } = true;
    public bool ShowWeeklyGauge { get; set; } = true;
    public bool ShowOpusGauge { get; set; } = true;
    public bool ShowSonnetGauge { get; set; } = true;

    public bool LoggingEnabled { get; set; }
    public string? LogDirectory { get; set; }

    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "usage-logs");

    public string EffectiveLogDirectory =>
        string.IsNullOrWhiteSpace(LogDirectory) ? DefaultLogDirectory : LogDirectory;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "usage-monitor-settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch
        {
            // fall through to default
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // best-effort
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
