using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClaudeUsageMonitor.Services;


namespace ClaudeUsageMonitor;

public partial class NoCredentialsWindow : Window
{
    public NoCredentialsWindow()
    {
        InitializeComponent();
        CredentialsPathRun.Text = UsageService.CredentialsLocation;
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }
}
