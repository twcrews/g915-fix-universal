namespace G915Fix.Core.Configuration;

/// <summary>Loads and saves an application-owned configuration document.</summary>
public interface IAppConfigurationStore
{
    string Path { get; }

    Task<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<ConfigurationSaveResult> SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed record ConfigurationLoadResult(AppConfiguration Configuration, bool Exists, string? Error = null)
{
    public bool Succeeded => Error is null;
}

public sealed record ConfigurationSaveResult(bool Succeeded, string? Error = null);
