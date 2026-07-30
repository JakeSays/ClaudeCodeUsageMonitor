using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeUsageMonitor.Controls;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;


namespace ClaudeUsageMonitor;

public partial class MainWindow : Window
{
    private static readonly double[] NotifyThresholds = [70, 80, 90];

    private readonly UsageService _usageService = new();
    private readonly UsageLogger _logger = new();
    private readonly UsageDatabase _database = new();
    private readonly Dictionary<string, double> _previousValues = new();
    private DispatcherTimer? _timer;

    public UsageDatabase Database => _database;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        PropertyChanged += OnPropertyChanged;
        ApplySettings();
    }

    private bool _handlingMinimize;

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_handlingMinimize || e.Property != WindowStateProperty)
        {
            return;
        }
        if (e.NewValue is not WindowState.Minimized)
        {
            return;
        }

        if (Application.Current is not App { Settings.MinimizeToTray: true })
        {
            return;
        }

        _handlingMinimize = true;
        try
        {
            Hide();
            WindowState = WindowState.Normal;
        }
        finally
        {
            _handlingMinimize = false;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _ = PollUsageAsync();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _timer.Tick += async (_, _) => await PollUsageAsync();
        _timer.Start();
    }

    public void OnSettingsChanged() => ApplySettings();

    private void OnChartsContextMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowChartsWindow();
        }
    }

    private void OnSettingsContextMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            _ = app.ShowSettingsWindow();
        }
    }

    private const double GaugeColumnWidth = 295;
    private const double WindowChromeWidth = 40;
    private static readonly TimeSpan BurnRateChartLookback = TimeSpan.FromDays(8);

    private void ApplySettings()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        var settings = app.Settings;

        Gauge5Hour.IsVisible = settings.ShowFiveHourGauge;
        GaugeWeekly.IsVisible = settings.ShowWeeklyGauge;
        GaugeOpus.IsVisible = settings.ShowOpusGauge;
        WeeklyLimitsPanel.IsVisible = settings.ShowWeeklyLimits;

        var visibleCount =
            (settings.ShowFiveHourGauge ? 1 : 0) +
            (settings.ShowWeeklyGauge ? 1 : 0) +
            (settings.ShowOpusGauge ? 1 : 0) +
            (settings.ShowWeeklyLimits ? 1 : 0);
        var columns = Math.Max(1, visibleCount);
        GaugeGrid.Columns = columns;
        Width = columns * GaugeColumnWidth + WindowChromeWidth;

        _logger.Enabled = settings.LoggingEnabled;
        _logger.LogOutputDirectory = settings.EffectiveLogDirectory;

        var showBurnRateChart = settings.WeeklyPanelMode == WeeklyPanelMode.BurnRateChart;
        WeeklyPanelHeader.Text = showBurnRateChart ? "Burn Rate Forecast" : "Weekly Limits";
        WeeklyLimitsList.IsVisible = !showBurnRateChart;
        BurnRateMiniPlot.IsVisible = showBurnRateChart;
        if (showBurnRateChart)
        {
            _ = UpdateBurnRateChartAsync();
        }
    }

    private async Task UpdateBurnRateChartAsync()
    {
        var since = DateTimeOffset.Now - BurnRateChartLookback;
        var samples = await Task.Run(() => _database.GetRange(since, DateTimeOffset.Now));
        BurnRateChartBuilder.Build(BurnRateMiniPlot.Plot, samples, compact: true);
        BurnRateMiniPlot.Refresh();
    }

    private async Task PollUsageAsync()
    {
        UsageResponse? usage = null;
        string? errorMessage = null;

        try
        {
            StatusText.Text = $"{DateTime.Now:g}";
            usage = await _usageService.GetUsageAsync();

            if (usage == null)
            {
                errorMessage = "No usage data received";
                SetError(errorMessage);
                return;
            }

            UpdateGauge(Gauge5Hour, usage.FiveHour);
            UpdateGauge(GaugeWeekly, usage.SevenDay);
            UpdateGauge(GaugeOpus, usage.SevenDayOpus);
            UpdateWeeklyLimits(usage.Limits);

            CheckThresholdCrossing("5-Hour", Gauge5Hour.Value);
            CheckThresholdCrossing("Weekly", GaugeWeekly.Value);
            CheckThresholdCrossing("Opus", GaugeOpus.Value);

            UpdateTrayTooltip();
            StatusText.Text = $"{DateTime.Now:g}";
            ErrorMessageText.Text = "";
        }
        catch (RateLimitedException ex)
        {
            var retryAt = DateTime.Now + ex.RetryAfter;
            errorMessage = $"Rate exceeded. Retrying at {retryAt:t}";
            SetError(errorMessage);
            if (_timer != null && ex.RetryAfter > _timer.Interval)
            {
                _timer.Interval = ex.RetryAfter;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            SetError(errorMessage);
        }
        finally
        {
            _logger.LogUpdate(usage, errorMessage);
            _database.Insert(usage, errorMessage);
            if (Application.Current is App { Settings.WeeklyPanelMode: WeeklyPanelMode.BurnRateChart })
            {
                _ = UpdateBurnRateChartAsync();
            }
        }
    }

    private static void UpdateGauge(GaugeControl gauge, UsageWindow? window)
    {
        if (window == null)
        {
            gauge.Value = 0;
            gauge.ResetText = "No data";
            gauge.ResetSubText = null;
            gauge.ErrorText = null;
            return;
        }

        gauge.Value = window.Utilization;
        gauge.ErrorText = null;

        if (window.ResetsAt != null)
        {
            var remaining = window.ResetsAt.Value - DateTimeOffset.UtcNow;
            if (remaining.TotalSeconds > 0)
            {
                gauge.ResetText = $"Resets on {window.ResetsAt.Value.LocalDateTime:M/d h:mm tt}";
                var totalHours = (int) Math.Floor(remaining.TotalHours);
                gauge.ResetSubText = totalHours >= 1
                    ? $"{totalHours}h {remaining.Minutes}m"
                    : $"{remaining.Minutes}m";
            }
            else
            {
                gauge.ResetText = "Resetting...";
                gauge.ResetSubText = null;
            }
        }
        else
        {
            gauge.ResetText = null;
            gauge.ResetSubText = null;
        }
    }

    private void CheckThresholdCrossing(string label, double current)
    {
        if (_previousValues.TryGetValue(label, out var previous))
        {
            foreach (var threshold in NotifyThresholds)
            {
                if (previous < threshold && current >= threshold)
                {
                    Notifier.Send(
                        $"{label} usage over {threshold:F0}%",
                        $"Current: {current:F0}%");
                }
            }
        }
        _previousValues[label] = current;
    }

    private void UpdateTrayTooltip()
    {
        var icons = TrayIcon.GetIcons(Application.Current!);
        if (icons == null || icons.Count == 0)
        {
            return;
        }

        icons[0].ToolTipText =
            $"5-Hour: {Gauge5Hour.Value:F0}%  Weekly: {GaugeWeekly.Value:F0}%  Opus: {GaugeOpus.Value:F0}%";
    }

    private void SetError(string message)
    {
        ErrorMessageText.Text = message;
        Gauge5Hour.ErrorText = "Error";
        GaugeWeekly.ErrorText = "Error";
        GaugeOpus.ErrorText = "Error";
    }

    private static readonly Color LimitNameColor = Color.FromRgb(224, 224, 240);
    private static readonly Color LimitDetailColor = Color.FromRgb(130, 130, 145);
    private static readonly Color LimitInactiveColor = Color.FromRgb(110, 110, 125);
    private static readonly Color LimitTrackColor = Color.FromRgb(51, 51, 77);

    private void UpdateWeeklyLimits(UsageLimit[]? limits)
    {
        WeeklyLimitsList.Children.Clear();

        // Only model-scoped entries — the unscoped weekly_all limit is already
        // the Weekly gauge.
        var weekly = limits?
            .Where(limit => limit.Group == "weekly" && limit.Scope?.Model != null)
            .OrderByDescending(limit => limit.Percent)
            .ToList();

        if (weekly == null || weekly.Count == 0)
        {
            WeeklyLimitsList.Children.Add(new TextBlock
            {
                Text = "No per-model limits reported",
                FontSize = 11,
                Foreground = new SolidColorBrush(LimitDetailColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var limit in weekly)
        {
            WeeklyLimitsList.Children.Add(BuildLimitRow(limit));
        }
    }

    private static Control BuildLimitRow(UsageLimit limit)
    {
        var nameText = new TextBlock
        {
            Text = limit.DisplayName,
            FontSize = 12,
            FontWeight = limit.IsActive ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = new SolidColorBrush(limit.IsActive ? LimitNameColor : LimitInactiveColor)
        };

        var percentText = new TextBlock
        {
            Text = $"{limit.Percent:F0}%",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(GaugeControl.ColorForValue(limit.Percent)),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        header.Children.Add(nameText);
        Grid.SetColumn(percentText, 1);
        header.Children.Add(percentText);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(limit.Percent, 0, 100),
            Height = 5,
            Margin = new Thickness(0, 3, 0, 0),
            Background = new SolidColorBrush(LimitTrackColor),
            Foreground = new SolidColorBrush(GaugeControl.ColorForValue(limit.Percent))
        };

        var row = new StackPanel();
        row.Children.Add(header);
        row.Children.Add(bar);

        var detail = limit.ResetsAt != null
            ? $"resets {limit.ResetsAt.Value.LocalDateTime:M/d h:mm tt}"
            : "no reset reported";
        row.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 10,
            Foreground = new SolidColorBrush(LimitDetailColor),
            Margin = new Thickness(0, 2, 0, 0)
        });

        return row;
    }
}
