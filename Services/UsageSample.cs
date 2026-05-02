using System;


namespace ClaudeUsageMonitor.Services;

public class UsageSample
{
    public DateTimeOffset Timestamp { get; set; }
    public double? FiveHourUtilization { get; set; }
    public DateTimeOffset? FiveHourResetsAt { get; set; }
    public double? WeeklyUtilization { get; set; }
    public DateTimeOffset? WeeklyResetsAt { get; set; }
    public double? OpusUtilization { get; set; }
    public DateTimeOffset? OpusResetsAt { get; set; }
    public double? SonnetUtilization { get; set; }
    public DateTimeOffset? SonnetResetsAt { get; set; }
    public bool? ExtraEnabled { get; set; }
    public double? ExtraMonthlyLimit { get; set; }
    public double? ExtraUsedCredits { get; set; }
    public double? ExtraUtilization { get; set; }
    public string? Error { get; set; }
}