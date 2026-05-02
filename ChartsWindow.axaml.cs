using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClaudeUsageMonitor.Services;
using ScottPlot;


namespace ClaudeUsageMonitor;

public partial class ChartsWindow : Window
{
    private readonly UsageDatabase _database;

    public ChartsWindow() : this(new UsageDatabase())
    {
    }

    public ChartsWindow(UsageDatabase database)
    {
        _database = database;
        InitializeComponent();
        Opened += (_, _) => RefreshAll();
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        try
        {
            var samples = _database.GetAll();
            StatusText.Text = $"{samples.Count} samples";

            BuildBurnRateForecast(samples);
            BuildHourOfDay(samples);
            BuildCycleOverlay(samples);
            BuildSonnetOpus(samples);
            BuildWaterfall(samples);
            BuildBurst(samples);
            BuildExtraCredits(samples);
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

    // ---- Chart 2: Hour-of-Day Heatmap ----
    // For each hour of day, average the rate-of-change of 5-hour utilization
    // (per minute). Bar chart 0..23.
    private void BuildHourOfDay(List<UsageSample> samples)
    {
        var plot = HourOfDayPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Avg 5-hour utilization growth by hour of day");
        plot.XLabel("Hour of day (local)");
        plot.YLabel("Δ util / minute");

        // Per-hour accumulators: sum-of-rates and count
        var sums = new double[24];
        var counts = new int[24];

        for (var i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];
            if (prev.FiveHourUtilization is not { } pu || curr.FiveHourUtilization is not { } cu)
            {
                continue;
            }
            // Reset event (or different cycle): util drops or resets_at changes
            if (cu < pu - 5 || prev.FiveHourResetsAt != curr.FiveHourResetsAt)
            {
                continue;
            }
            var dtMinutes = (curr.Timestamp - prev.Timestamp).TotalMinutes;
            if (dtMinutes <= 0 || dtMinutes > 30)
            {
                continue;
            }
            var rate = (cu - pu) / dtMinutes;
            var hour = curr.Timestamp.LocalDateTime.Hour;
            sums[hour] += rate;
            counts[hour]++;
        }

        var avgs = new double[24];
        for (var h = 0; h < 24; h++)
        {
            avgs[h] = counts[h] > 0 ? sums[h] / counts[h] : 0;
        }

        var bars = plot.Add.Bars(Enumerable.Range(0, 24).Select(h => (double) h).ToArray(), avgs);
        bars.Color = Colors.MediumPurple;

        plot.Axes.SetLimits(-0.5, 23.5, 0, avgs.DefaultIfEmpty(1).Max() * 1.15);
        HourOfDayPlot.Refresh();
    }

    // ---- Chart 3: Cycle Overlay ----
    // Plot weekly utilization vs hours-since-reset, one line per completed
    // weekly cycle, plus the in-progress cycle highlighted.
    private void BuildCycleOverlay(List<UsageSample> samples)
    {
        var plot = CycleOverlayPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Weekly cycles overlay (anchored at reset)");
        plot.XLabel("Hours since cycle start");
        plot.YLabel("Utilization %");

        var byReset = samples
            .Where(s => s.WeeklyUtilization != null && s.WeeklyResetsAt != null)
            .GroupBy(s => s.WeeklyResetsAt!.Value)
            .OrderBy(g => g.Key)
            .ToList();

        if (byReset.Count == 0)
        {
            plot.Add.Annotation("No data yet.", Alignment.MiddleCenter);
            CycleOverlayPlot.Refresh();
            return;
        }

        var nowResetGroup = byReset[^1];
        var palette = new ScottPlot.Palettes.Category10();

        for (var i = 0; i < byReset.Count; i++)
        {
            var group = byReset[i].OrderBy(s => s.Timestamp).ToList();
            // The cycle started 7 days before reset
            var cycleStart = byReset[i].Key - TimeSpan.FromDays(7);
            var hoursSince = group.Select(s => (s.Timestamp - cycleStart).TotalHours).ToArray();
            var utilizations = group.Select(s => s.WeeklyUtilization ?? 0).ToArray();

            var line = plot.Add.Scatter(hoursSince, utilizations);
            line.MarkerSize = 0;
            var isCurrent = ReferenceEquals(byReset[i], nowResetGroup);
            line.LineWidth = isCurrent ? 3 : 1;
            line.Color = isCurrent
                ? Colors.OrangeRed
                : palette.GetColor(i % 10).WithAlpha(0.55);
            line.LegendText = isCurrent
                ? $"Current (resets {byReset[i].Key.LocalDateTime:M/d})"
                : $"Cycle {byReset[i].Key.LocalDateTime:M/d}";
        }

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        plot.Axes.SetLimits(0, 168, 0, 110);
        plot.ShowLegend();
        CycleOverlayPlot.Refresh();
    }

    // ---- Chart 4: Sonnet vs Opus split ----
    // Within the current weekly cycle, plot sonnet and opus utilization as
    // separate lines. Also draws total weekly for reference.
    private void BuildSonnetOpus(List<UsageSample> samples)
    {
        var plot = SonnetOpusPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("Sonnet vs Opus utilization (current cycle)");
        plot.XLabel("Time");
        plot.YLabel("Utilization %");

        var current = CurrentCycleSamples(samples);
        if (current.Count == 0)
        {
            plot.Add.Annotation("No data in current cycle yet.", Alignment.MiddleCenter);
            SonnetOpusPlot.Refresh();
            return;
        }

        var xs = current.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();

        if (current.Any(s => s.WeeklyUtilization != null))
        {
            var ys = current.Select(s => s.WeeklyUtilization ?? 0).ToArray();
            var line = plot.Add.Scatter(xs, ys);
            line.LegendText = "Total weekly";
            line.MarkerSize = 0;
            line.Color = Colors.LightGray;
            line.LineWidth = 1.5f;
        }
        if (current.Any(s => s.OpusUtilization != null))
        {
            var ys = current.Select(s => s.OpusUtilization ?? 0).ToArray();
            var line = plot.Add.Scatter(xs, ys);
            line.LegendText = "Opus";
            line.MarkerSize = 0;
            line.Color = Colors.OrangeRed;
            line.LineWidth = 2;
        }
        if (current.Any(s => s.SonnetUtilization != null))
        {
            var ys = current.Select(s => s.SonnetUtilization ?? 0).ToArray();
            var line = plot.Add.Scatter(xs, ys);
            line.LegendText = "Sonnet";
            line.MarkerSize = 0;
            line.Color = Colors.SteelBlue;
            line.LineWidth = 2;
        }

        UseDateTimeBottomAxis(plot);
        plot.ShowLegend();
        SonnetOpusPlot.Refresh();
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
            .GroupBy(s => s.WeeklyResetsAt!.Value)
            .OrderBy(g => g.Key)
            .ToList();

        // Drop the most recent (in-progress) cycle from the bar chart, but
        // include it as a translucent bar so it's visible.
        var values = new List<double>();
        var positions = new List<double>();
        var labels = new List<string>();

        for (var i = 0; i < byReset.Count; i++)
        {
            var lastSample = byReset[i].OrderBy(s => s.Timestamp).Last();
            values.Add(lastSample.WeeklyUtilization ?? 0);
            positions.Add(i);
            labels.Add(byReset[i].Key.LocalDateTime.ToString("M/d"));
        }

        if (values.Count == 0)
        {
            plot.Add.Annotation("No completed cycles yet.", Alignment.MiddleCenter);
            WaterfallPlot.Refresh();
            return;
        }

        var bars = new List<Bar>();
        for (var i = 0; i < values.Count; i++)
        {
            var isCurrent = i == values.Count - 1;
            bars.Add(new Bar
            {
                Position = positions[i],
                Value = values[i],
                FillColor = isCurrent ? Colors.OrangeRed.WithAlpha(0.6) : Colors.MediumPurple,
                Label = $"{values[i]:F0}%"
            });
        }
        plot.Add.Bars(bars);

        plot.Add.HorizontalLine(100, color: Colors.Red.WithAlpha(0.4));
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.ToArray(), labels.ToArray());
        plot.Axes.SetLimits(-0.5, positions[^1] + 0.5, 0, Math.Max(110, values.Max() + 10));
        WaterfallPlot.Refresh();
    }

    // ---- Chart 6: Burst Detector ----
    // 5-hour utilization rate-of-change over time, with a threshold line
    // marking "burst" sessions.
    private void BuildBurst(List<UsageSample> samples)
    {
        var plot = BurstPlot.Plot;
        plot.Clear();
        StyleDarkPlot(plot);
        plot.Title("5-hour utilization burn rate (Δ%/min)");
        plot.XLabel("Time");
        plot.YLabel("Δ util / minute");

        var xs = new List<double>();
        var ys = new List<double>();

        for (var i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var curr = samples[i];
            if (prev.FiveHourUtilization is not { } pu || curr.FiveHourUtilization is not { } cu)
            {
                continue;
            }
            if (cu < pu - 5 || prev.FiveHourResetsAt != curr.FiveHourResetsAt)
            {
                continue;
            }
            var dtMinutes = (curr.Timestamp - prev.Timestamp).TotalMinutes;
            if (dtMinutes <= 0 || dtMinutes > 30)
            {
                continue;
            }
            var rate = Math.Max(0, (cu - pu) / dtMinutes);
            xs.Add(curr.Timestamp.LocalDateTime.ToOADate());
            ys.Add(rate);
        }

        if (xs.Count == 0)
        {
            plot.Add.Annotation("No data yet.", Alignment.MiddleCenter);
            BurstPlot.Refresh();
            return;
        }

        var line = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
        line.MarkerSize = 0;
        line.Color = Colors.SkyBlue;
        line.LineWidth = 1;

        // Threshold = 2x the median nonzero rate
        var nonzero = ys.Where(v => v > 0).OrderBy(v => v).ToList();
        if (nonzero.Count > 0)
        {
            var median = nonzero[nonzero.Count / 2];
            var threshold = median * 2;
            var thresholdLine = plot.Add.HorizontalLine(threshold);
            thresholdLine.Color = Colors.Orange;
            thresholdLine.LineStyle.Pattern = LinePattern.Dashed;
            thresholdLine.LegendText = $"Burst threshold ({threshold:F2}/min)";

            // Mark points exceeding threshold
            var burstXs = new List<double>();
            var burstYs = new List<double>();
            for (var i = 0; i < xs.Count; i++)
            {
                if (ys[i] >= threshold)
                {
                    burstXs.Add(xs[i]);
                    burstYs.Add(ys[i]);
                }
            }
            if (burstXs.Count > 0)
            {
                var marks = plot.Add.Scatter(burstXs.ToArray(), burstYs.ToArray());
                marks.LineWidth = 0;
                marks.MarkerSize = 6;
                marks.Color = Colors.OrangeRed;
                marks.LegendText = "Bursts";
            }
        }

        UseDateTimeBottomAxis(plot);
        plot.ShowLegend();
        BurstPlot.Refresh();
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

    private static List<UsageSample> CurrentCycleSamples(List<UsageSample> samples)
    {
        var withReset = samples.Where(s => s.WeeklyResetsAt != null).ToList();
        if (withReset.Count == 0)
        {
            return new List<UsageSample>();
        }
        var latestReset = withReset[^1].WeeklyResetsAt;
        return withReset.Where(s => s.WeeklyResetsAt == latestReset).ToList();
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
