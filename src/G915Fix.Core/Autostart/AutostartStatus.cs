namespace G915Fix.Core.Autostart;

/// <summary>The current state of this application's login-start registration.</summary>
public enum AutostartStatus
{
    /// <summary>This application has a valid autostart registration.</summary>
    Enabled,

    /// <summary>This application has no autostart registration.</summary>
    Disabled,

    /// <summary>
    /// A registration with the application's identity exists but is not owned by
    /// this application. It must not be changed without explicit user action.
    /// </summary>
    Conflict,

    /// <summary>The current platform does not support application autostart.</summary>
    NotSupported,

    /// <summary>The platform requires the user to approve or finish setup.</summary>
    RequiresUserAction,

    /// <summary>The platform could not determine the registration state.</summary>
    Unknown
}
