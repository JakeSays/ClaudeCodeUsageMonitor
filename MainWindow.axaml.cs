using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
        GaugeSonnet.IsVisible = settings.ShowSonnetGauge;

        var visibleCount =
            (settings.ShowFiveHourGauge ? 1 : 0) +
            (settings.ShowWeeklyGauge ? 1 : 0) +
            (settings.ShowOpusGauge ? 1 : 0) +
            (settings.ShowSonnetGauge ? 1 : 0);
        var columns = Math.Max(1, visibleCount);
        GaugeGrid.Columns = columns;
        Width = columns * GaugeColumnWidth + WindowChromeWidth;

        _logger.Enabled = settings.LoggingEnabled;
        _logger.LogOutputDirectory = settings.EffectiveLogDirectory;
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
            UpdateGauge(GaugeSonnet, usage.SevenDaySonnet);

            CheckThresholdCrossing("5-Hour", Gauge5Hour.Value);
            CheckThresholdCrossing("Weekly", GaugeWeekly.Value);
            CheckThresholdCrossing("Opus", GaugeOpus.Value);
            CheckThresholdCrossing("Sonnet", GaugeSonnet.Value);

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
        }
    }

    private static void UpdateGauge(GaugeControl gauge, UsageWindow? window)
    {
        if (window == null)
        {
            gauge.Value = 0;
            gauge.ResetText = "No data";
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
                gauge.ResetText = remaining.TotalHours >= 1
                    ? $"Resets in {remaining.Hours + (int) remaining.TotalDays * 24}h {remaining.Minutes}m"
                    : $"Resets in {remaining.Minutes}m";
            }
            else
            {
                gauge.ResetText = "Resetting...";
            }
        }
        else
        {
            gauge.ResetText = null;
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
            $"5-Hour: {Gauge5Hour.Value:F0}%  Weekly: {GaugeWeekly.Value:F0}%  Opus: {GaugeOpus.Value:F0}%  Sonnet: {GaugeSonnet.Value:F0}%";
    }

    private void SetError(string message)
    {
        ErrorMessageText.Text = message;
        Gauge5Hour.ErrorText = "Error";
        GaugeWeekly.ErrorText = "Error";
        GaugeOpus.ErrorText = "Error";
        GaugeSonnet.ErrorText = "Error";
    }
}
