namespace G915Fix.Core.Input;

/// <summary>
/// The input filter's access state for the currently focused application.
/// </summary>
public enum ForegroundInputAccessStatus
{
    /// <summary>The filter can process input for the focused application.</summary>
    Available,

    /// <summary>The platform has confirmed that the filter is bypassed.</summary>
    Bypassed,

    /// <summary>The platform could not determine whether the filter has access.</summary>
    Unknown,

    /// <summary>The platform has no meaningful foreground-access detection capability.</summary>
    NotSupported
}
