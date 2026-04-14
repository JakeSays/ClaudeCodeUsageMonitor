using Avalonia.Labs.Notifications;


namespace ClaudeUsageMonitor.Services;

public static class Notifier
{
    public static void Send(string title, string body)
    {
        var manager = NativeNotificationManager.Current;
        if (manager == null)
        {
            return;
        }

        var notification = manager.CreateNotification("usage");
        if (notification == null)
        {
            return;
        }

        notification.Title = title;
        notification.Message = body;
        notification.Show();
    }
}
