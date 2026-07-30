using ScottPlot;


namespace ClaudeUsageMonitor;

// Shared dark styling for every ScottPlot instance in the app, so the mini
// burn rate panel on MainWindow and the full ChartsWindow stay visually consistent.
public static class PlotTheme
{
    private static readonly Color FigureBackgroundColor = Color.FromHex("#1a1a2e");
    private static readonly Color DataBackgroundColor = Color.FromHex("#22223a");
    private static readonly Color AxisForegroundColor = Color.FromHex("#b0b0c0");
    private static readonly Color GridLineColor = Color.FromHex("#33334d");
    private static readonly Color LegendBackgroundColor = Color.FromHex("#22223a");
    private static readonly Color LegendForegroundColor = Color.FromHex("#e0e0f0");
    private static readonly Color LegendOutlineColor = Color.FromHex("#444466");

    public static void StyleDarkPlot(Plot plot)
    {
        plot.FigureBackground.Color = FigureBackgroundColor;
        plot.DataBackground.Color = DataBackgroundColor;
        plot.Axes.Color(AxisForegroundColor);
        plot.Grid.MajorLineColor = GridLineColor;
        plot.Legend.BackgroundColor = LegendBackgroundColor;
        plot.Legend.FontColor = LegendForegroundColor;
        plot.Legend.OutlineColor = LegendOutlineColor;
    }

    public static void UseDateTimeBottomAxis(Plot plot)
    {
        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Bottom.TickLabelStyle.ForeColor = AxisForegroundColor;
        plot.Axes.Bottom.MajorTickStyle.Color = AxisForegroundColor;
        plot.Axes.Bottom.MinorTickStyle.Color = AxisForegroundColor;
        plot.Axes.Bottom.FrameLineStyle.Color = AxisForegroundColor;
    }
}
