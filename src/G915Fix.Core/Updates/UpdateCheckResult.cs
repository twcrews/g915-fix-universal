namespace G915Fix.Core.Updates;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version? LatestVersion = null,
    Uri? ReleaseUri = null,
    string? Message = null);
