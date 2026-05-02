using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClaudeUsageMonitor.Services;


namespace ClaudeUsageMonitor;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public bool Saved { get; private set; }

    public SettingsWindow() : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        LogDirectoryBox.PlaceholderText = $"Default: {AppSettings.DefaultLogDirectory}";

        ShowFiveHourCheck.IsChecked = settings.ShowFiveHourGauge;
        ShowWeeklyCheck.IsChecked = settings.ShowWeeklyGauge;
        ShowOpusCheck.IsChecked = settings.ShowOpusGauge;
        ShowSonnetCheck.IsChecked = settings.ShowSonnetGauge;
        LoggingEnabledCheck.IsChecked = settings.LoggingEnabled;
        LogDirectoryBox.Text = settings.LogDirectory ?? string.Empty;
        MinimizeToTrayCheck.IsChecked = settings.MinimizeToTray;
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var startPath = string.IsNullOrWhiteSpace(LogDirectoryBox.Text)
            ? AppSettings.DefaultLogDirectory
            : LogDirectoryBox.Text;

        IStorageFolder? startFolder = null;
        try
        {
            startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startPath);
        }
        catch
        {
            // ignore — fall back to default location picker
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose log directory",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            LogDirectoryBox.Text = path;
        }
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        _settings.ShowFiveHourGauge = ShowFiveHourCheck.IsChecked == true;
        _settings.ShowWeeklyGauge = ShowWeeklyCheck.IsChecked == true;
        _settings.ShowOpusGauge = ShowOpusCheck.IsChecked == true;
        _settings.ShowSonnetGauge = ShowSonnetCheck.IsChecked == true;
        _settings.LoggingEnabled = LoggingEnabledCheck.IsChecked == true;
        _settings.LogDirectory = string.IsNullOrWhiteSpace(LogDirectoryBox.Text)
            ? null
            : LogDirectoryBox.Text.Trim();
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

        _settings.Save();
        Saved = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close();
    }
}
