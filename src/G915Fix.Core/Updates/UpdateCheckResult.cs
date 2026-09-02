namespace G915Fix.Core.Updates;

/// <summary>The result of a best-effort update check.</summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion = null,
    Uri? ReleaseUri = null,
    string? Message = null)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;
}
