using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;


namespace ClaudeUsageMonitor.Controls;

public class GaugeControl : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Value));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<GaugeControl, string>(nameof(Label), "Usage");

    public static readonly StyledProperty<string?> ResetTextProperty =
        AvaloniaProperty.Register<GaugeControl, string?>(nameof(ResetText));

    public static readonly StyledProperty<string?> ResetSubTextProperty =
        AvaloniaProperty.Register<GaugeControl, string?>(nameof(ResetSubText));

    public static readonly StyledProperty<bool> IsRetryingProperty =
        AvaloniaProperty.Register<GaugeControl, bool>(nameof(IsRetrying));

    static GaugeControl()
    {
        AffectsRender<GaugeControl>(ValueProperty, LabelProperty, ResetTextProperty, ResetSubTextProperty, IsRetryingProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? ResetText
    {
        get => GetValue(ResetTextProperty);
        set => SetValue(ResetTextProperty, value);
    }

    public string? ResetSubText
    {
        get => GetValue(ResetSubTextProperty);
        set => SetValue(ResetSubTextProperty, value);
    }

    public bool IsRetrying
    {
        get => GetValue(IsRetryingProperty);
        set => SetValue(IsRetryingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
        {
            return;
        }
        var centerX = bounds.Width / 2;
        var centerY = bounds.Height / 2 - 10;
        var radius = size * 0.38;
        var thickness = size * 0.06;

        // Arc spans from 225 degrees (lower-left) to -45 degrees (lower-right) = 270 degree sweep
        const double startAngleDeg = 135;
        const double sweepDeg = 270;
        var value = Math.Clamp(Value, 0, 100);
        var filledSweep = sweepDeg * (value / 100.0);

        // Draw background arc
        var bgPen = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 70)), thickness, lineCap: PenLineCap.Round);
        DrawArc(
            context, centerX, centerY, radius, startAngleDeg,
            sweepDeg, bgPen);

        // Draw filled arc as a green-to-yellow-to-red gradient over 0-100%
        if (value > 0)
        {
            DrawGradientArc(
                context, centerX, centerY, radius, startAngleDeg,
                filledSweep, sweepDeg, thickness);
        }

        // Draw tick marks at 25% intervals
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 110)), 1.5);
        for (var i = 1; i <= 3; i++)
        {
            var tickAngle = startAngleDeg + sweepDeg * (i / 4.0);
            var tickRad = tickAngle * Math.PI / 180;
            var innerR = radius - thickness * 0.8;
            var outerR = radius + thickness * 0.8;
            context.DrawLine(
                tickPen,
                new Point(centerX + innerR * Math.Cos(tickRad), centerY + innerR * Math.Sin(tickRad)),
                new Point(centerX + outerR * Math.Cos(tickRad), centerY + outerR * Math.Sin(tickRad)));
        }

        // Draw percentage text in center
        var percentText = new FormattedText(
            $"{value:F0}%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            size * 0.16,
            new SolidColorBrush(Colors.White));
        context.DrawText(
            percentText,
            new Point(centerX - percentText.Width / 2, centerY - percentText.Height / 2));

        // Draw label below gauge
        var labelText = new FormattedText(
            Label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            size * 0.075,
            new SolidColorBrush(Color.FromRgb(180, 180, 190)));
        var labelY = centerY + radius - 1;
        context.DrawText(
            labelText,
            new Point(centerX - labelText.Width / 2, labelY));

        // Draw reset text below label
        var line1 = ResetText;
        if (line1 == null)
        {
            return;
        }

        var line1Color = IsRetrying
            ? Color.FromRgb(150, 40, 40)
            : Color.FromRgb(180, 180, 190);
        var line1Text = new FormattedText(
            line1,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            size * 0.058,
            new SolidColorBrush(line1Color));
        var line1Y = labelY + labelText.Height + 2;
        context.DrawText(
            line1Text,
            new Point(centerX - line1Text.Width / 2, line1Y));

        var line2 = ResetSubText;
        if (line2 == null)
        {
            return;
        }

        var line2Color = IsRetrying
            ? Color.FromRgb(150, 40, 40)
            : Color.FromRgb(130, 130, 145);
        var line2Text = new FormattedText(
            line2,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            size * 0.065,
            new SolidColorBrush(line2Color));
        context.DrawText(
            line2Text,
            new Point(centerX - line2Text.Width / 2, line1Y + line1Text.Height + 1));
    }

    public static Color ColorForValue(double value)
    {
        return value switch
        {
            < 50 => Color.FromRgb(76, 175, 80),
            < 75 => Color.FromRgb(255, 193, 7),
            < 90 => Color.FromRgb(255, 152, 0),
            _ => Color.FromRgb(244, 67, 54)
        };
    }

    private static void DrawArc(
        DrawingContext context,
        double cx,
        double cy,
        double r,
        double startDeg,
        double sweepDeg,
        Pen pen)
    {
        // Approximate arc with line segments
        const int segments = 100;
        var startRad = startDeg * Math.PI / 180;
        var sweepRad = sweepDeg * Math.PI / 180;

        for (var i = 0; i < segments; i++)
        {
            var a1 = startRad + sweepRad * i / segments;
            var a2 = startRad + sweepRad * (i + 1) / segments;
            context.DrawLine(
                pen,
                new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)),
                new Point(cx + r * Math.Cos(a2), cy + r * Math.Sin(a2)));
        }
    }

    // The full 270-degree sweep represents 0-100%, so slicing it into 100
    // segments lets each segment's own percent drive its gradient color --
    // the color at a given angle stays fixed as the fill grows or shrinks.
    private static void DrawGradientArc(
        DrawingContext context,
        double cx,
        double cy,
        double r,
        double startDeg,
        double filledSweepDeg,
        double totalSweepDeg,
        double thickness)
    {
        const int totalSegments = 100;
        var startRad = startDeg * Math.PI / 180;
        var totalSweepRad = totalSweepDeg * Math.PI / 180;
        var filledSegments = Math.Clamp(
            (int) Math.Round(totalSegments * (filledSweepDeg / totalSweepDeg)),
            0, totalSegments);

        for (var i = 0; i < filledSegments; i++)
        {
            var a1 = startRad + totalSweepRad * i / totalSegments;
            var a2 = startRad + totalSweepRad * (i + 1) / totalSegments;
            var segmentPercent = (i + 0.5) / totalSegments * 100.0;
            var pen = new Pen(
                new SolidColorBrush(GradientColorForValue(segmentPercent)),
                thickness, lineCap: PenLineCap.Round);
            context.DrawLine(
                pen,
                new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)),
                new Point(cx + r * Math.Cos(a2), cy + r * Math.Sin(a2)));
        }
    }

    private static readonly Color GradientGreen = Color.FromRgb(76, 175, 80);
    private static readonly Color GradientYellow = Color.FromRgb(255, 235, 59);
    private static readonly Color GradientRed = Color.FromRgb(244, 67, 54);

    private static Color GradientColorForValue(double percent)
    {
        return percent <= 50
            ? LerpColor(GradientGreen, GradientYellow, percent / 50.0)
            : LerpColor(GradientYellow, GradientRed, (percent - 50) / 50.0);
    }

    private static Color LerpColor(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte) (from.R + (to.R - from.R) * t),
            (byte) (from.G + (to.G - from.G) * t),
            (byte) (from.B + (to.B - from.B) * t));
    }
}
