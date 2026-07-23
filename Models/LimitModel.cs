using System.Text.Json.Serialization;


namespace ClaudeUsageMonitor.Models;

public class LimitModel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}
