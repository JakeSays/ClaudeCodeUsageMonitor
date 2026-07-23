using System.Text.Json.Serialization;


namespace ClaudeUsageMonitor.Models;

public class LimitScope
{
    [JsonPropertyName("model")]
    public LimitModel? Model { get; set; }

    [JsonPropertyName("surface")]
    public string? Surface { get; set; }
}
