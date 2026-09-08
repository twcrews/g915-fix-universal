using G915Fix.Core.Autostart;
using G915Fix.Core.Input;
using G915Fix.Core.Notifications;
using G915Fix.Core.Permissions;
using G915Fix.Core.Profiles;
using G915Fix.Core.Updates;

namespace G915Fix.Desktop.Services;

/// <summary>
/// Portable services supplied by an OS-specific host to the shared desktop UI.
/// Native capture, consent prompts, and login-item registration stay in the host.
/// </summary>
public sealed class DesktopApplicationServices
{
    public DesktopApplicationServices(
        IInputFilterRuntime inputRuntime,
        IAppProfileService profiles,
        IPermissionService permissions,
        IAutostartService autostart,
        IUpdateChecker? updateChecker = null,
        IUserNotificationService? notifications = null)
    {
        InputRuntime = inputRuntime ?? throw new ArgumentNullException(nameof(inputRuntime));
        Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        Autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        UpdateChecker = updateChecker;
        Notifications = notifications;
    }

    public IInputFilterRuntime InputRuntime { get; }
    public IAppProfileService Profiles { get; }
    public IPermissionService Permissions { get; }
    public IAutostartService Autostart { get; }
    public IUpdateChecker? UpdateChecker { get; }
    public IUserNotificationService? Notifications { get; }
}

/// <summary>Host-specific presentation values that do not belong in Core configuration.</summary>
public sealed class DesktopHostOptions
{
    public DesktopHostOptions(Version currentVersion, string applicationName = "G915 Fix")
    {
        CurrentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        ApplicationName = string.IsNullOrWhiteSpace(applicationName)
            ? throw new ArgumentException("Application name is required.", nameof(applicationName))
            : applicationName;
    }

    public Version CurrentVersion { get; }
    public string ApplicationName { get; }
}
