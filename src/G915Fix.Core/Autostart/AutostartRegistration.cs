namespace G915Fix.Core.Autostart;

/// <summary>
/// Describes the application's current login-start registration and any
/// platform-specific detail suitable for display or logging.
/// </summary>
public sealed record AutostartRegistration(
    AutostartStatus Status,
    string? Message = null)
{
    public bool IsEnabled => Status == AutostartStatus.Enabled;
}
