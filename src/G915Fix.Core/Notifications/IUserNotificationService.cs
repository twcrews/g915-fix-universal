namespace G915Fix.Core.Notifications;

public interface IUserNotificationService
{
    Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default);
}
