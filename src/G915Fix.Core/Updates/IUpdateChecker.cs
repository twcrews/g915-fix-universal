namespace G915Fix.Core.Updates;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}
