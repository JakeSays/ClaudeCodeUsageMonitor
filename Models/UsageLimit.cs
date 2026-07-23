using System;
using System.Text.Json.Serialization;


namespace ClaudeUsageMonitor.Models;

public class UsageLimit
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }

    [JsonPropertyName("scope")]
    public LimitScope? Scope { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    // The API names a scoped limit only through its scope; unscoped limits
    // carry their identity in the kind.
    public string DisplayName => Scope?.Model?.DisplayName ?? Kind ?? "unknown";
}
