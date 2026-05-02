using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ClaudeUsageMonitor.Models;


namespace ClaudeUsageMonitor.Services;

public class UsageDatabase : IDisposable
{
    private static readonly string DefaultDatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "usage-monitor.db");

    private readonly SqliteConnection _connection;

    public UsageDatabase() : this(DefaultDatabasePath)
    {
    }

    public UsageDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path};");
        _connection.Open();
        InitializeSchema();
    }

    public void Dispose() => _connection.Dispose();

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Samples (
                Timestamp           INTEGER PRIMARY KEY,
                FiveHourUtilization REAL,
                FiveHourResetsAt    INTEGER,
                WeeklyUtilization   REAL,
                WeeklyResetsAt      INTEGER,
                OpusUtilization     REAL,
                OpusResetsAt        INTEGER,
                SonnetUtilization   REAL,
                SonnetResetsAt      INTEGER,
                ExtraEnabled        INTEGER,
                ExtraMonthlyLimit   REAL,
                ExtraUsedCredits    REAL,
                ExtraUtilization    REAL,
                Error               TEXT
            );

            CREATE INDEX IF NOT EXISTS WeeklyResetsAtIndex
                ON Samples(WeeklyResetsAt);
            CREATE INDEX IF NOT EXISTS FiveHourResetsAtIndex
                ON Samples(FiveHourResetsAt);
        ";
        cmd.ExecuteNonQuery();
    }

    public void Insert(UsageResponse? usage, string? error)
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Samples (
                    Timestamp,
                    FiveHourUtilization, FiveHourResetsAt,
                    WeeklyUtilization, WeeklyResetsAt,
                    OpusUtilization, OpusResetsAt,
                    SonnetUtilization, SonnetResetsAt,
                    ExtraEnabled, ExtraMonthlyLimit, ExtraUsedCredits, ExtraUtilization,
                    Error
                ) VALUES (
                    $Timestamp,
                    $FiveHourUtilization, $FiveHourResetsAt,
                    $WeeklyUtilization, $WeeklyResetsAt,
                    $OpusUtilization, $OpusResetsAt,
                    $SonnetUtilization, $SonnetResetsAt,
                    $ExtraEnabled, $ExtraMonthlyLimit, $ExtraUsedCredits, $ExtraUtilization,
                    $Error
                );
            ";

            cmd.Parameters.AddWithValue("$Timestamp", DateTimeOffset.Now.Ticks);
            cmd.Parameters.AddWithValue("$FiveHourUtilization",
                (object?) usage?.FiveHour?.Utilization ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$FiveHourResetsAt",
                usage?.FiveHour?.ResetsAt is { } fiveHourResetsAt ? fiveHourResetsAt.ToLocalTime().Ticks : DBNull.Value);
            cmd.Parameters.AddWithValue("$WeeklyUtilization",
                (object?) usage?.SevenDay?.Utilization ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$WeeklyResetsAt",
                usage?.SevenDay?.ResetsAt is { } weeklyResetsAt ? weeklyResetsAt.ToLocalTime().Ticks : DBNull.Value);
            cmd.Parameters.AddWithValue("$OpusUtilization",
                (object?) usage?.SevenDayOpus?.Utilization ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$OpusResetsAt",
                usage?.SevenDayOpus?.ResetsAt is { } opusResetsAt ? opusResetsAt.ToLocalTime().Ticks : DBNull.Value);
            cmd.Parameters.AddWithValue("$SonnetUtilization",
                (object?) usage?.SevenDaySonnet?.Utilization ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$SonnetResetsAt",
                usage?.SevenDaySonnet?.ResetsAt is { } sonnetResetsAt ? sonnetResetsAt.ToLocalTime().Ticks : DBNull.Value);
            cmd.Parameters.AddWithValue("$ExtraEnabled",
                usage?.ExtraUsage?.IsEnabled is { } extraEnabled ? (extraEnabled ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("$ExtraMonthlyLimit",
                (object?) usage?.ExtraUsage?.MonthlyLimit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ExtraUsedCredits",
                (object?) usage?.ExtraUsage?.UsedCredits ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ExtraUtilization",
                (object?) usage?.ExtraUsage?.Utilization ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Error", (object?) error ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        catch
        {
            // best-effort; don't fail the poll on a DB issue
        }
    }

    public List<UsageSample> GetRange(DateTimeOffset from, DateTimeOffset to)
    {
        var results = new List<UsageSample>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Timestamp,
                   FiveHourUtilization, FiveHourResetsAt,
                   WeeklyUtilization, WeeklyResetsAt,
                   OpusUtilization, OpusResetsAt,
                   SonnetUtilization, SonnetResetsAt,
                   ExtraEnabled, ExtraMonthlyLimit, ExtraUsedCredits, ExtraUtilization,
                   Error
            FROM Samples
            WHERE Timestamp BETWEEN $From AND $To
            ORDER BY Timestamp ASC;
        ";
        cmd.Parameters.AddWithValue("$From", from.ToLocalTime().Ticks);
        cmd.Parameters.AddWithValue("$To", to.ToLocalTime().Ticks);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSample(reader));
        }
        return results;
    }

    public List<UsageSample> GetAll()
    {
        var results = new List<UsageSample>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Timestamp,
                   FiveHourUtilization, FiveHourResetsAt,
                   WeeklyUtilization, WeeklyResetsAt,
                   OpusUtilization, OpusResetsAt,
                   SonnetUtilization, SonnetResetsAt,
                   ExtraEnabled, ExtraMonthlyLimit, ExtraUsedCredits, ExtraUtilization,
                   Error
            FROM Samples
            ORDER BY Timestamp ASC;
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSample(reader));
        }
        return results;
    }

    public List<long> GetDistinctWeeklyResets()
    {
        var results = new List<long>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT WeeklyResetsAt
            FROM Samples
            WHERE WeeklyResetsAt IS NOT NULL
            ORDER BY WeeklyResetsAt ASC;
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetInt64(0));
        }
        return results;
    }

    private static UsageSample ReadSample(SqliteDataReader r) => new()
    {
        Timestamp = LocalFromTicks(r.GetInt64(0)),
        FiveHourUtilization = r.IsDBNull(1) ? null : r.GetDouble(1),
        FiveHourResetsAt = r.IsDBNull(2) ? null : LocalFromTicks(r.GetInt64(2)),
        WeeklyUtilization = r.IsDBNull(3) ? null : r.GetDouble(3),
        WeeklyResetsAt = r.IsDBNull(4) ? null : LocalFromTicks(r.GetInt64(4)),
        OpusUtilization = r.IsDBNull(5) ? null : r.GetDouble(5),
        OpusResetsAt = r.IsDBNull(6) ? null : LocalFromTicks(r.GetInt64(6)),
        SonnetUtilization = r.IsDBNull(7) ? null : r.GetDouble(7),
        SonnetResetsAt = r.IsDBNull(8) ? null : LocalFromTicks(r.GetInt64(8)),
        ExtraEnabled = r.IsDBNull(9) ? null : r.GetInt32(9) != 0,
        ExtraMonthlyLimit = r.IsDBNull(10) ? null : r.GetDouble(10),
        ExtraUsedCredits = r.IsDBNull(11) ? null : r.GetDouble(11),
        ExtraUtilization = r.IsDBNull(12) ? null : r.GetDouble(12),
        Error = r.IsDBNull(13) ? null : r.GetString(13)
    };

    private static DateTimeOffset LocalFromTicks(long ticks) =>
        new(new DateTime(ticks, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(new DateTime(ticks, DateTimeKind.Unspecified)));
}
