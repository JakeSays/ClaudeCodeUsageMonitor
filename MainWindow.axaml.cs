using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    private readonly Dictionary<string, double> _previousValues = new();
    private DispatcherTimer? _timer;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        PropertyChanged += OnPropertyChanged;
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
            Interval = TimeSpan.FromMinutes(1)
        };
        _timer.Tick += async (_, _) => await PollUsageAsync();
        _timer.Start();
    }

    private async Task PollUsageAsync()
    {
        try
        {
            StatusText.Text = $"{DateTime.Now:g}";
            var usage = await _usageService.GetUsageAsync();

            if (usage == null)
            {
                SetError("No usage data received");
                return;
            }

            UpdateGauge(Gauge5Hour, usage.FiveHour);
            UpdateGauge(GaugeWeekly, usage.SevenDay);
            UpdateGauge(GaugeSonnet, usage.SevenDaySonnet);

            CheckThresholdCrossing("5-Hour", Gauge5Hour.Value);
            CheckThresholdCrossing("Weekly", GaugeWeekly.Value);
            CheckThresholdCrossing("Sonnet", GaugeSonnet.Value);

            UpdateTrayTooltip();
            StatusText.Text = $"{DateTime.Now:g}";
        }
        catch (RateLimitedException ex)
        {
            var retryAt = DateTime.Now + ex.RetryAfter;
            SetError($"Rate exceeded. Retrying at {retryAt:t}");
            if (_timer != null && ex.RetryAfter > _timer.Interval)
            {
                _timer.Interval = ex.RetryAfter;
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
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
            $"5-Hour: {Gauge5Hour.Value:F0}%  Weekly: {GaugeWeekly.Value:F0}%  Sonnet: {GaugeSonnet.Value:F0}%";
    }

    private void SetError(string message)
    {
        StatusText.Text = $"Error: {message}";
        Gauge5Hour.ErrorText = "Error";
        GaugeWeekly.ErrorText = "Error";
        GaugeSonnet.ErrorText = "Error";
    }
}
