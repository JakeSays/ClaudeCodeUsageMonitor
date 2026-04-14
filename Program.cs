using System;
using System.Threading;
using Avalonia;
using Avalonia.Labs.Notifications;


namespace ClaudeUsageMonitor;

internal class Program
{
    private static Mutex? _singleInstanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "ClaudeUsageMonitor.SingleInstance", out var acquired);
        if (!acquired)
        {
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithAppNotifications(new AppNotificationOptions
            {
                AppName = "Claude Usage Monitor"
            })
            .LogToTrace();
}
