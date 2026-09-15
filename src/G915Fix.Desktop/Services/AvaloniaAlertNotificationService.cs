using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using G915Fix.Core.Notifications;

namespace G915Fix.Desktop.Services;

/// <summary>Displays a notification in a small, dismissible Avalonia alert window.</summary>
public sealed class AvaloniaAlertNotificationService : IUserNotificationService
{
    public async Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
        {
            Show(notification);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Show(notification));
    }

    private static void Show(UserNotification notification)
    {
        var alert = new Window
        {
            Title = notification.Title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var dismiss = new Button
        {
            Content = "OK",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        dismiss.Click += (_, _) => alert.Close();

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16
        };
        content.Children.Add(new TextBlock
        {
            Text = notification.Message,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(dismiss);
        alert.Content = content;
        alert.Show();
    }
}
