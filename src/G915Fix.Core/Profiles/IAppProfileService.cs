using G915Fix.Core.Configuration;

namespace G915Fix.Core.Profiles;

/// <summary>Application-profile lifecycle suitable for UI binding and game automation.</summary>
public interface IAppProfileService : IProfileActivationService<AppConfiguration>
{
    ProfileDescriptor BaseProfile { get; }

    Task<IReadOnlyList<ProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<ProfileActivationResult> InitializeAsync(CancellationToken cancellationToken = default);

    Task<ProfileActivationResult> ActivateAsync(
        ProfileDescriptor profile,
        bool persistAsDefault,
        CancellationToken cancellationToken = default);

    Task<ConfigurationSaveResult> SaveActiveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public sealed record ProfileActivationResult(
    bool Succeeded,
    ProfileDescriptor? ActiveProfile,
    AppConfiguration? ActiveConfiguration,
    string? Message = null);
