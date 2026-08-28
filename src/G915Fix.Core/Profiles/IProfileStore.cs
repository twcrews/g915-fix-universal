namespace G915Fix.Core.Profiles;

public interface IProfileStore<TConfig>
{
    Task<IReadOnlyList<ProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<TConfig> LoadAsync(ProfileDescriptor profile, CancellationToken cancellationToken = default);

    Task SaveAsync(ProfileDescriptor profile, TConfig config, CancellationToken cancellationToken = default);
}
