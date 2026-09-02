namespace G915Fix.Core.Autostart;

/// <summary>
/// Manages this application's user-level login-start registration. Platform
/// implementations own the launch command, registration format, and any
/// required user-consent flow.
/// </summary>
public interface IAutostartService
{
    Task<AutostartRegistration> GetRegistrationAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables this application's autostart registration. Implementations must
    /// not overwrite a conflicting registration.
    /// </summary>
    Task<AutostartRegistration> EnableAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables only an autostart registration owned by this application.
    /// </summary>
    Task<AutostartRegistration> DisableAsync(
        CancellationToken cancellationToken = default);
}
