namespace G915Fix.Core.Notifications;

public sealed record UserNotification(
    string Title,
    string Message,
    NotificationSeverity Severity = NotificationSeverity.Info,
    TimeSpan? Duration = null);
