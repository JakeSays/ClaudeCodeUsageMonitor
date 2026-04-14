using System.Text.Json.Serialization;

namespace ClaudeUsageMonitor.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UsageResponse))]
[JsonSerializable(typeof(CredentialsFile))]
internal partial class UsageJsonContext : JsonSerializerContext;