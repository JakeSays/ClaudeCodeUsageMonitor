using System;
using System.Globalization;
using System.IO;
using System.Text;
using ClaudeUsageMonitor.Models;


namespace ClaudeUsageMonitor.Services;

public class UsageLogger : IDisposable
{
    private const string Header =
        "Timestamp\tFiveHourUtilization\tFiveHourResetsAt\tWeeklyUtilization\tWeeklyResetsAt\t" +
        "OpusUtilization\tOpusResetsAt\tSonnetUtilization\tSonnetResetsAt\t" +
        "ExtraEnabled\tExtraMonthlyLimit\tExtraUsedCredits\tExtraUtilization\tError";

    private StreamWriter? _writer;
    private DateTime _currentDate;
    private string? _currentDirectory;

    public bool Enabled { get; set; }

    public string LogOutputDirectory { get; set; } = AppSettings.DefaultLogDirectory;

    public void LogUpdate(UsageResponse? usage, string? error)
    {
        if (!Enabled)
        {
            CloseWriter();
            return;
        }

        try
        {
            var now = DateTime.Now;
            EnsureWriter(now);

            if (_writer == null)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append(now.ToString("o", CultureInfo.InvariantCulture));
            AppendWindow(sb, usage?.FiveHour);
            AppendWindow(sb, usage?.SevenDay);
            AppendWindow(sb, usage?.SevenDayOpus);
            AppendWindow(sb, usage?.SevenDaySonnet);
            AppendExtra(sb, usage?.ExtraUsage);
            sb.Append('\t');
            sb.Append(EscapeField(error));

            _writer.WriteLine(sb.ToString());
            _writer.Flush();
        }
        catch
        {
            // best-effort logging
            CloseWriter();
        }
    }

    public void Dispose() => CloseWriter();

    private void EnsureWriter(DateTime now)
    {
        var today = now.Date;
        if (_writer != null && _currentDate == today && _currentDirectory == LogOutputDirectory)
        {
            return;
        }

        CloseWriter();

        Directory.CreateDirectory(LogOutputDirectory);
        var fileName = $"usage-{today:yyyy-MM-dd}.tsv";
        var path = Path.Combine(LogOutputDirectory, fileName);
        var existed = File.Exists(path);

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
        _currentDate = today;
        _currentDirectory = LogOutputDirectory;

        if (existed)
        {
            return;
        }

        _writer.WriteLine(Header);
        _writer.Flush();
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // ignore
        }
        _writer = null;
        _currentDate = default;
        _currentDirectory = null;
    }

    private static void AppendWindow(StringBuilder sb, UsageWindow? window)
    {
        sb.Append('\t');
        if (window == null)
        {
            sb.Append('\t');
            return;
        }
        sb.Append(window.Utilization.ToString("F2", CultureInfo.InvariantCulture));
        sb.Append('\t');
        if (window.ResetsAt != null)
        {
            sb.Append(window.ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    private static void AppendExtra(StringBuilder sb, ExtraUsage? extra)
    {
        sb.Append('\t');
        if (extra == null)
        {
            sb.Append("\t\t\t");
            return;
        }
        sb.Append(extra.IsEnabled ? "true" : "false");
        sb.Append('\t');
        if (extra.MonthlyLimit != null)
        {
            sb.Append(extra.MonthlyLimit.Value.ToString("F2", CultureInfo.InvariantCulture));
        }
        sb.Append('\t');
        if (extra.UsedCredits != null)
        {
            sb.Append(extra.UsedCredits.Value.ToString("F2", CultureInfo.InvariantCulture));
        }
        sb.Append('\t');
        if (extra.Utilization != null)
        {
            sb.Append(extra.Utilization.Value.ToString("F2", CultureInfo.InvariantCulture));
        }
    }

    private static string EscapeField(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? ""
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
