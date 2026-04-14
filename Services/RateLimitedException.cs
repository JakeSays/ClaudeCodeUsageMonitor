using System;


namespace ClaudeUsageMonitor.Services;

public class RateLimitedException(TimeSpan retryAfter)
    : Exception($"Rate limited; retry after {retryAfter.TotalSeconds:F0}s")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
