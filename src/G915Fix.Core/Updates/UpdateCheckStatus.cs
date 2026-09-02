namespace G915Fix.Core.Updates;

/// <summary>The outcome of an update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The latest release is not newer than the running version.</summary>
    UpToDate,

    /// <summary>A newer release is available.</summary>
    UpdateAvailable,

    /// <summary>The latest release could not be determined.</summary>
    Failed
}
