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

    public static readonly StyledProperty<string?> ErrorTextProperty =
        AvaloniaProperty.Register<GaugeControl, string?>(nameof(ErrorText));

    static GaugeControl()
    {
        AffectsRender<GaugeControl>(ValueProperty, LabelProperty, ResetTextProperty, ErrorTextProperty);
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

    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
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

        // Draw filled arc with color based on value
        if (value > 0)
        {
            var gaugeColor = GetGaugeColor(value);
            var fillPen = new Pen(new SolidColorBrush(gaugeColor), thickness, lineCap: PenLineCap.Round);
            DrawArc(
                context, centerX, centerY, radius, startAngleDeg,
                filledSweep, fillPen);
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
        context.DrawText(
            labelText,
            new Point(centerX - labelText.Width / 2, centerY + radius + thickness + 8));

        // Draw reset text or error text below label
        var bottomText = ErrorText ?? ResetText;
        if (bottomText == null)
        {
            return;
        }

        var color = ErrorText != null
            ? Color.FromRgb(255, 100, 100)
            : Color.FromRgb(130, 130, 145);
        var resetFormattedText = new FormattedText(
            bottomText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            size * 0.058,
            new SolidColorBrush(color));
        context.DrawText(
            resetFormattedText,
            new Point(
                centerX - resetFormattedText.Width / 2, centerY + radius + thickness + 8 + labelText.Height + 4));
    }

    private static Color GetGaugeColor(double value)
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
}
