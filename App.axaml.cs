using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClaudeUsageMonitor.Services;


namespace ClaudeUsageMonitor;

public class App : Application
{
    public AppSettings Settings { get; } = AppSettings.Load();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!UsageService.CredentialsFileExists())
            {
                var icons = TrayIcon.GetIcons(this);
                if (icons != null)
                {
                    foreach (var icon in icons)
                    {
                        icon.IsVisible = false;
                    }
                }

                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                desktop.MainWindow = new NoCredentialsWindow();
                desktop.MainWindow.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Show();
        }

        var menu = FindMinimizeMenuItem();
        menu?.IsChecked = Settings.MinimizeToTray;

        base.OnFrameworkInitializationCompleted();
    }

    private NativeMenuItem? FindMinimizeMenuItem()
    {
        var icons = TrayIcon.GetIcons(this);
        if (icons == null || icons.Count == 0 || icons[0].Menu == null)
        {
            return null;
        }

        foreach (var item in icons[0].Menu!.Items)
        {
            if (item is NativeMenuItem { Header: "Minimize to tray" } m)
            {
                return m;
            }
        }
        return null;
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnMinimizeToTrayMenuClicked(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem item)
        {
            return;
        }

        Settings.MinimizeToTray = !Settings.MinimizeToTray;
        item.IsChecked = Settings.MinimizeToTray;
        Settings.Save();
    }

    private void OnQuitMenuClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var window = desktop.MainWindow ??= new MainWindow();
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
        });
    }
}
