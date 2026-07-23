using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClaudeUsageMonitor.Services;
using ScottPlot;
using ScottPlot.Avalonia;


namespace ClaudeUsageMonitor;

public partial class ChartsWindow : Window
{
    private readonly UsageDatabase _database;

    public ChartsWindow()
        : this(new UsageDatabase())
    {
    }

    private List<UsageSample> _samples = [];
    private readonly HashSet<int> _builtTabs = [];
    private bool _samplesLoaded;

    public ChartsWindow(UsageDatabase database)
    {
        _database = database;
        InitializeComponent();

        // Index matches tab order.
        _plots =
        [
            BurnRatePlot,
            DailyUsagePlot,
            WeeklyUsagePlot,
            UsageHistoryPlot,
            WaterfallPlot,
            FiveHourPlot,
            ExtraCreditsPlot
        ];

        // An unbuilt plot paints as an empty default chart, so keep each one
        // hidden until it holds real data rather than showing it get redrawn.
        foreach (var control in _plots)
        {
            StyleDarkPlot(control.Plot);
            control.IsVisible = false;
        }

        ChartsTabs.SelectionChanged += (_, _) => BuildSelectedTab();
        Opened += (_, _) => _ = RefreshAllAsync();
    }

    private readonly AvaPlot[] _plots;

    private void OnRefreshClicked(object? sender, RoutedEventArgs e) => _ = RefreshAllAsync();

    // Reading and parsing the whole table takes long enough to be visible, so it
    // runs off the UI thread and only the tab in view gets built; the rest are
    // built the first time they are selected.
    private async Task RefreshAllAsync()
    {
        try
        {
            StatusText.Text = "Loading...";
            _samples = await Task.Run(_database.GetAll);
            _samplesLoaded = true;
            StatusText.Text = $"{_samples.Count} samples";

            _builtTabs.Clear();
            BuildSelectedTab();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void BuildSelectedTab()
    {
        if (!_samplesLoaded)
        {
            return;
        }

        var index = ChartsTabs.SelectedIndex;
        if (index < 0 || !_builtTabs.Add(index))
        {
            return;
        }

        try
        {
            switch (index)
            {
                case 0:
                    BuildBurnRateForecast(_samples);
                    break;
                case 1:
                    BuildDailyUsage(_samples);
                    break;
                case 2:
                    BuildWeeklyUsage(_samples);
                    break;
                case 3:
                    BuildUsageHistory(_samples);
                    break;
                case 4:
                    BuildWaterfall(_samples);
                    break;
                case 5:
                    BuildFiveHourWindow(_samples);
                    break;
                case 6:
                    BuildExtraCredits(_samples);
                    break;
            }

            _plots[index].IsVisible = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    // ---- Chart 1: Burn Rate Forecast ----
    // Linear-fit the most recent samples within the current weekly cycle, then
    // project forward to find the time the line crosses 100%. Compare to reset.
    private void BuildBurnRateForecast(List<UsageSample> samples)
    {
        var plot = BurnRatePlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly burn rate forecast (current cycle)");
        plot.XLabel("Time");
        plot.YLabel("Utilization %");

        var current = CurrentCycleSamples(samples);
        if (current.Count < 2)
        {
            plot.Add.Annotation("Not enough data in current cycle yet.", Alignment.MiddleCenter);
            BurnRatePlot.Refresh();
            return;
        }

        var xs = current.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
        var ys = current.Select(s => s.WeeklyUtilization ?? 0).ToArray();

        var actual = plot.Add.Scatter(xs, ys);
        actual.LegendText = "Actual";
        actual.Color = Colors.SkyBlue;
        actual.LineWidth = 2;
        actual.MarkerSize = 4;

        // Linear fit on the most recent 24h (or all if shorter)
        var fitWindowStart = current[^1].Timestamp.AddHours(-24);
        var fitSlice = current.Where(s => s.Timestamp >= fitWindowStart).ToList();
        if (fitSlice.Count >= 2)
        {
            var fitXs = fitSlice.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
            var fitYs = fitSlice.Select(s => s.WeeklyUtilization ?? 0).ToArray();
            var (slope, intercept) = LinearFit(fitXs, fitYs);

            var resetX = current[^1].WeeklyResetsAt?.LocalDateTime.ToOADate() ?? fitXs[^1] + 7;
            var hit100X = slope > 0 ? (100 - intercept) / slope : double.NaN;
            var endX = double.IsNaN(hit100X) ? resetX : Math.Min(hit100X + 0.5, resetX);
            endX = Math.Max(endX, fitXs[^1]);

            var lineXs = new[] { fitXs[0], endX };
            var lineYs = new[] { slope * fitXs[0] + intercept, slope * endX + intercept };
            var forecast = plot.Add.Scatter(lineXs, lineYs);
            forecast.LegendText = "Forecast";
            forecast.LineStyle.Pattern = LinePattern.Dashed;
            forecast.Color = Colors.Orange;
            forecast.LineWidth = 2;
            forecast.MarkerSize = 0;

            if (!double.IsNaN(hit100X) && slope > 0)
            {
                var hitTime = DateTime.FromOADate(hit100X);
                var resetTime = current[^1].WeeklyResetsAt?.LocalDateTime;
                var note = resetTime.HasValue
                    ? hit100X < resetX
                        ? $"Hits 100% at {hitTime:M/d HH:mm} — {(resetTime.Value - hitTime).TotalHours:F1}h before reset"
                        : $"Won't hit 100% before reset ({resetTime:M/d HH:mm})"
                    : $"Hits 100% at {hitTime:M/d HH:mm}";
                plot.Add.Annotation(note, Alignment.UpperLeft);
            }

            // Vertical line at reset
            var resetLine = plot.Add.VerticalLine(resetX);
            resetLine.Color = Colors.Red.WithAlpha(0.5);
            resetLine.LineStyle.Pattern = LinePattern.Dotted;
            resetLine.LegendText = "Reset";
        }

        // Horizontal line at 100%
        var capLine = plot.Add.HorizontalLine(100);
        capLine.Color = Colors.Red.WithAlpha(0.4);
        capLine.LineStyle.Pattern = LinePattern.Dotted;

        UseDateTimeBottomAxis(plot);
        plot.ShowLegend();
        BurnRatePlot.Refresh();
    }

    // ---- Chart 2: Daily Usage ----
    // Bar chart of weekly utilization % consumed per calendar day.
    // Within each cycle, the gain per day = max util that day - max util previous day.
    private void BuildDailyUsage(List<UsageSample> samples)
    {
        var plot = DailyUsagePlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly utilization consumed per day");
        plot.XLabel("Date");
        plot.YLabel("Utilization % gained");

        var cycleGroups = samples
            .Where(s => s.WeeklyUtilization != null && s.WeeklyResetsAt != null)
            .GroupBy(s => s.WeeklyResetsAt!.Value.Date)
            .OrderBy(g => g.Key);

        var dailyGains = new List<(DateTime Date, double Gain)>();
        foreach (var cycle in cycleGroups)
        {
            var byDay = cycle
                .GroupBy(s => s.Timestamp.LocalDateTime.Date)
                .OrderBy(g => g.Key)
                .ToList();
            var prevMax = 0.0;
            foreach (var dayGroup in byDay)
            {
                var dayMax = dayGroup.Max(s => s.WeeklyUtilization!.Value);
                dailyGains.Add((dayGroup.Key, Math.Max(0, dayMax - prevMax)));
                prevMax = dayMax;
            }
        }

        if (dailyGains.Count == 0)
        {
            plot.Add.Annotation("No data yet.", Alignment.MiddleCenter);
            DailyUsagePlot.Refresh();
            return;
        }

        var bars = new List<Bar>();
        var positions = new List<double>();
        var labels = new List<string>();
        for (var i = 0; i < dailyGains.Count; i++)
        {
            bars.Add(new Bar { Position = i, Value = dailyGains[i].Gain, FillColor = Colors.SteelBlue });
            positions.Add(i);
            labels.Add(dailyGains[i].Date.ToString("M/d"));
        }
        plot.Add.Bars(bars);
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.ToArray(), labels.ToArray());
        var maxGain = dailyGains.Max(d => d.Gain);
        plot.Axes.SetLimits(-0.5, dailyGains.Count - 0.5, 0, Math.Max(maxGain * 1.15, 5));
        DailyUsagePlot.Refresh();
    }

    // ---- Chart 3: Usage History ----
    // Weekly utilization as a continuous line over all recorded time,
    // with dotted vertical markers at each cycle reset.
    private void BuildUsageHistory(List<UsageSample> samples)
    {
        var plot = UsageHistoryPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly utilization over time");
        plot.XLabel("Time");
        plot.YLabel("Utilization %");

        var withWeekly = samples
            .Where(s => s.WeeklyUtilization != null)
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (withWeekly.Count == 0)
        {
            plot.Add.Annotation("No data yet.", Alignment.MiddleCenter);
            UsageHistoryPlot.Refresh();
            return;
        }

        var xs = withWeekly.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
        var ys = withWeekly.Select(s => s.WeeklyUtilization!.Value).ToArray();

        var line = plot.Add.Scatter(xs, ys);
        line.MarkerSize = 0;
        line.Color = Colors.SkyBlue;
        line.LineWidth = 2;

        var pastResets = withWeekly
            .Where(s => s.WeeklyResetsAt != null && s.WeeklyResetsAt < DateTimeOffset.Now)
            .Select(s => s.WeeklyResetsAt!.Value.Date)
            .Distinct()
            .OrderBy(d => d);

        foreach (var resetDate in pastResets)
        {
            var vl = plot.Add.VerticalLine(resetDate.ToOADate());
            vl.Color = Colors.Red.WithAlpha(0.35);
            vl.LineStyle.Pattern = LinePattern.Dotted;
        }

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        UseDateTimeBottomAxis(plot);
        UsageHistoryPlot.Refresh();
    }

    // ---- Chart 4: Weekly Usage ----
    // Weekly utilization across the current cycle, with the reset marked.
    private void BuildWeeklyUsage(List<UsageSample> samples)
    {
        var plot = WeeklyUsagePlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly utilization (current cycle)");
        plot.XLabel("Time");
        plot.YLabel("Utilization %");

        var current = CurrentCycleSamples(samples)
            .Where(s => s.WeeklyUtilization != null)
            .ToList();

        if (current.Count == 0)
        {
            plot.Add.Annotation("No data in current cycle yet.", Alignment.MiddleCenter);
            WeeklyUsagePlot.Refresh();
            return;
        }

        var xs = current.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
        var ys = current.Select(s => s.WeeklyUtilization!.Value).ToArray();

        var line = plot.Add.Scatter(xs, ys);
        line.MarkerSize = 0;
        line.Color = Colors.SkyBlue;
        line.LineWidth = 2;

        var resetsAt = current[^1].WeeklyResetsAt;
        if (resetsAt != null)
        {
            var resetLine = plot.Add.VerticalLine(resetsAt.Value.LocalDateTime.ToOADate());
            resetLine.Color = Colors.Red.WithAlpha(0.5);
            resetLine.LineStyle.Pattern = LinePattern.Dotted;
            resetLine.LegendText = $"Resets {resetsAt.Value.LocalDateTime:M/d h:mm tt}";
            plot.ShowLegend();
        }

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        UseDateTimeBottomAxis(plot);
        WeeklyUsagePlot.Refresh();
    }

    // ---- Chart 5: Reset Waterfall ----
    // For each completed weekly cycle, the final utilization at reset.
    private void BuildWaterfall(List<UsageSample> samples)
    {
        var plot = WaterfallPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly utilization at reset");
        plot.XLabel("Cycle reset date");
        plot.YLabel("Final utilization %");

        var byReset = samples
            .Where(s => s.WeeklyUtilization != null && s.WeeklyResetsAt != null)
            .GroupBy(s => s.WeeklyResetsAt!.Value.Date)
            .OrderBy(g => g.Key)
            .ToList();

        // Drop the most recent (in-progress) cycle from the bar chart, but
        // include it as a translucent bar so it's visible.
        if (byReset.Count == 0)
        {
            plot.Add.Annotation("No completed cycles yet.", Alignment.MiddleCenter);
            WaterfallPlot.Refresh();
            return;
        }

        var bars = new List<Bar>();
        var positions = new List<double>();
        var labels = new List<string>();
        for (var i = 0; i < byReset.Count; i++)
        {
            var lastSample = byReset[i].OrderBy(s => s.Timestamp).Last();
            var value = lastSample.WeeklyUtilization ?? 0;
            var isCurrent = i == byReset.Count - 1;
            bars.Add(new Bar
            {
                Position = i,
                Value = value,
                FillColor = isCurrent ? Colors.OrangeRed.WithAlpha(0.6) : Colors.MediumPurple,
                Label = $"{value:F0}%"
            });
            positions.Add(i);
            labels.Add(byReset[i].Key.ToString("M/d"));
        }
        plot.Add.Bars(bars);

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.ToArray(), labels.ToArray());
        var maxValue = bars.Max(b => b.Value);
        plot.Axes.SetLimits(-0.5, positions[^1] + 0.5, 0, Math.Max(110, maxValue + 10));
        WaterfallPlot.Refresh();
    }

    // ---- Chart 6: 5-Hour Window ----
    // Raw 5-hour utilization over all recorded time. The sawtooth shape
    // reveals when active work happened (steep rises) and when windows reset.
    private void BuildFiveHourWindow(List<UsageSample> samples)
    {
        var plot = FiveHourPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("5-hour window utilization over time");
        plot.XLabel("Time");
        plot.YLabel("Utilization %");

        var withFiveHour = samples
            .Where(s => s.FiveHourUtilization != null)
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (withFiveHour.Count == 0)
        {
            plot.Add.Annotation("No data yet.", Alignment.MiddleCenter);
            FiveHourPlot.Refresh();
            return;
        }

        var xs = withFiveHour.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
        var ys = withFiveHour.Select(s => s.FiveHourUtilization!.Value).ToArray();

        var line = plot.Add.Scatter(xs, ys);
        line.MarkerSize = 0;
        line.Color = Colors.SkyBlue;
        line.LineWidth = 1.5f;

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        UseDateTimeBottomAxis(plot);
        FiveHourPlot.Refresh();
    }

    // ---- Chart 7: Extra Credits ----
    // Top: progress bar of UsedCredits / MonthlyLimit with month-end forecast.
    // Bottom: daily incremental spend bars for the current calendar month,
    // overlaid with the daily-average pace needed to exactly hit the limit.
    private void BuildExtraCredits(List<UsageSample> samples)
    {
        const double MinimumFractionalDayForPace = 0.01;
        const double DefaultBarUpperPaddingMultiplier = 1.2;
        const double PaceLineHeadroomMultiplier = 1.5;

        var latest = samples.LastOrDefault(s => s.ExtraEnabled != null);
        var enabled = latest?.ExtraEnabled == true;
        var monthlyLimit = latest?.ExtraMonthlyLimit ?? 0;
        var usedCredits = latest?.ExtraUsedCredits ?? 0;
        var utilization = latest?.ExtraUtilization ?? 0;

        var plot = ExtraCreditsPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);

        if (!enabled || monthlyLimit <= 0)
        {
            ExtraCreditsHeader.Text = "Extra Credits";
            ExtraCreditsAmount.Text = "Extra usage is not enabled on this account.";
            ExtraCreditsProgressBar.Value = 0;
            ExtraCreditsForecast.Text = "";
            plot.Title("Extra credits — not enabled");
            ExtraCreditsPlot.Refresh();
            return;
        }

        var today = DateTime.Now;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var fractionalDay = today.Day - 1 + today.Hour / 24.0 + today.Minute / 1440.0;
        if (fractionalDay < MinimumFractionalDayForPace)
        {
            fractionalDay = MinimumFractionalDayForPace;
        }
        var projectedTotal = usedCredits * daysInMonth / fractionalDay;
        var dailyPace = monthlyLimit / daysInMonth;

        ExtraCreditsHeader.Text = "Extra Credits";
        ExtraCreditsAmount.Text =
            $"{usedCredits:F2} of {monthlyLimit:F2} credits used ({utilization:F1}%)";
        ExtraCreditsProgressBar.Value = Math.Clamp(utilization, 0, 100);

        var forecastSummary = projectedTotal > monthlyLimit
            ? $"On pace to spend ~{projectedTotal:F2} this month — {projectedTotal - monthlyLimit:F2} over the {monthlyLimit:F2} cap."
            : $"On pace to spend ~{projectedTotal:F2} this month — {monthlyLimit - projectedTotal:F2} under the {monthlyLimit:F2} cap.";
        ExtraCreditsForecast.Text = $"Day {today.Day} of {daysInMonth}. {forecastSummary}";

        plot.Title("Daily extra credits spend (current month)");
        plot.XLabel("Day of month");
        plot.YLabel("Credits");

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthSamples = samples
            .Where(s => s.Timestamp.LocalDateTime >= monthStart && s.ExtraUsedCredits != null)
            .ToList();

        if (monthSamples.Count == 0)
        {
            plot.Add.Annotation("No samples in current month yet.", Alignment.MiddleCenter);
            ExtraCreditsPlot.Refresh();
            return;
        }

        var maxUsedPerDay = new double?[daysInMonth];
        foreach (var sample in monthSamples)
        {
            var dayIndex = sample.Timestamp.LocalDateTime.Day - 1;
            var value = sample.ExtraUsedCredits!.Value;
            if (!maxUsedPerDay[dayIndex].HasValue || value > maxUsedPerDay[dayIndex]!.Value)
            {
                maxUsedPerDay[dayIndex] = value;
            }
        }

        var bars = new List<Bar>();
        var maxDailyDelta = 0.0;
        var priorCumulative = 0.0;
        for (var dayIndex = 0; dayIndex < daysInMonth; dayIndex++)
        {
            if (!maxUsedPerDay[dayIndex].HasValue)
            {
                continue;
            }
            var cumulative = maxUsedPerDay[dayIndex]!.Value;
            var delta = Math.Max(0, cumulative - priorCumulative);
            bars.Add(new Bar
            {
                Position = dayIndex + 1,
                Value = delta,
                FillColor = Colors.Teal
            });
            if (delta > maxDailyDelta)
            {
                maxDailyDelta = delta;
            }
            priorCumulative = cumulative;
        }

        plot.Add.Bars(bars);

        var paceLine = plot.Add.HorizontalLine(dailyPace);
        paceLine.LineStyle.Pattern = LinePattern.Dashed;
        paceLine.Color = Colors.Orange;
        paceLine.LegendText = $"Daily pace to hit cap ({dailyPace:F2}/day)";

        var todayMarker = plot.Add.VerticalLine(today.Day);
        todayMarker.Color = Colors.Yellow.WithAlpha(0.4);
        todayMarker.LineStyle.Pattern = LinePattern.Dotted;
        todayMarker.LegendText = "Today";

        var yUpperBound = Math.Max(
            dailyPace * PaceLineHeadroomMultiplier,
            maxDailyDelta * DefaultBarUpperPaddingMultiplier);
        if (yUpperBound <= 0)
        {
            yUpperBound = dailyPace * PaceLineHeadroomMultiplier;
        }
        plot.Axes.SetLimits(0.5, daysInMonth + 0.5, 0, yUpperBound);
        plot.ShowLegend();
        ExtraCreditsPlot.Refresh();
    }

    // ---- Helpers ----

    // The API's resets_at drifts by a fraction of a second between polls, so a
    // cycle has to be identified by its reset day — matching the exact value
    // pairs a sample only with itself.
    private static List<UsageSample> CurrentCycleSamples(List<UsageSample> samples)
    {
        var withReset = samples.Where(s => s.WeeklyResetsAt != null).ToList();
        if (withReset.Count == 0)
        {
            return new List<UsageSample>();
        }
        var latestResetDay = withReset[^1].WeeklyResetsAt!.Value.Date;
        return withReset.Where(s => s.WeeklyResetsAt!.Value.Date == latestResetDay).ToList();
    }

    private static (double slope, double intercept) LinearFit(double[] xs, double[] ys)
    {
        var n = xs.Length;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            sx += xs[i];
            sy += ys[i];
            sxx += xs[i] * xs[i];
            sxy += xs[i] * ys[i];
        }
        var denom = n * sxx - sx * sx;
        if (denom == 0)
        {
            return (0, sy / n);
        }
        var slope = (n * sxy - sx * sy) / denom;
        var intercept = (sy - slope * sx) / n;
        return (slope, intercept);
    }

    private static readonly Color FigureBackgroundColor = Color.FromHex("#1a1a2e");
    private static readonly Color DataBackgroundColor = Color.FromHex("#22223a");
    private static readonly Color AxisForegroundColor = Color.FromHex("#b0b0c0");
    private static readonly Color GridLineColor = Color.FromHex("#33334d");
    private static readonly Color LegendBackgroundColor = Color.FromHex("#22223a");
    private static readonly Color LegendForegroundColor = Color.FromHex("#e0e0f0");
    private static readonly Color LegendOutlineColor = Color.FromHex("#444466");

    private static void StyleDarkPlot(Plot plot)
    {
        plot.FigureBackground.Color = FigureBackgroundColor;
        plot.DataBackground.Color = DataBackgroundColor;
        plot.Axes.Color(AxisForegroundColor);
        plot.Grid.MajorLineColor = GridLineColor;
        plot.Legend.BackgroundColor = LegendBackgroundColor;
        plot.Legend.FontColor = LegendForegroundColor;
        plot.Legend.OutlineColor = LegendOutlineColor;
    }

    private static void UseDateTimeBottomAxis(Plot plot)
    {
        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Bottom.TickLabelStyle.ForeColor = AxisForegroundColor;
        plot.Axes.Bottom.MajorTickStyle.Color = AxisForegroundColor;
        plot.Axes.Bottom.MinorTickStyle.Color = AxisForegroundColor;
        plot.Axes.Bottom.FrameLineStyle.Color = AxisForegroundColor;
    }
}
