using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeUsageMonitor.Services;
using ScottPlot;


namespace ClaudeUsageMonitor;

// Shared by ChartsWindow's full-size "Burn Rate Forecast" tab and MainWindow's
// compact panel version, so the forecast math and rendering stay in one place.
public static class BurnRateChartBuilder
{
    public static void Build(Plot plot, List<UsageSample> samples, bool compact = false)
    {
        plot.Clear();
        PlotTheme.StyleDarkPlot(plot);

        if (!compact)
        {
            plot.Title("Weekly burn rate forecast (current cycle)");
            plot.XLabel("Time");
            plot.YLabel("Utilization %");
        }

        var current = CurrentCycleSamples(samples);
        if (current.Count < 2)
        {
            plot.Add.Annotation("Not enough data in current cycle yet.", Alignment.MiddleCenter);
            return;
        }

        var xs = current.Select(s => s.Timestamp.LocalDateTime.ToOADate()).ToArray();
        var ys = current.Select(s => s.WeeklyUtilization ?? 0).ToArray();

        var actual = plot.Add.Scatter(xs, ys);
        actual.LegendText = "Actual";
        actual.Color = Colors.SkyBlue;
        actual.LineWidth = compact ? 1.5f : 2;
        actual.MarkerSize = compact ? 0 : 4;

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
            forecast.LineWidth = compact ? 1.5f : 2;
            forecast.MarkerSize = 0;

            if (!compact && !double.IsNaN(hit100X) && slope > 0)
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

        PlotTheme.UseDateTimeBottomAxis(plot);
        if (compact)
        {
            plot.Axes.Bottom.TickLabelStyle.FontSize = 9;
            plot.Axes.Left.TickLabelStyle.FontSize = 9;
            plot.Legend.IsVisible = false;
        }
        else
        {
            plot.ShowLegend();
        }
    }

    // The API's resets_at drifts by a fraction of a second between polls, so a
    // cycle has to be identified by its reset day — matching the exact value
    // pairs a sample only with itself.
    public static List<UsageSample> CurrentCycleSamples(List<UsageSample> samples)
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
}
