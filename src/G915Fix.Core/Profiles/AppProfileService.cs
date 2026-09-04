using G915Fix.Core.Configuration;

namespace G915Fix.Core.Profiles;

/// <summary>
/// Coordinates the base configuration, persisted default profile, and active
/// profile. All file locations are supplied by the caller through the store.
/// </summary>
public sealed class AppProfileService : IAppProfileService
{
    private readonly IProfileStore<AppConfiguration> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppConfiguration? _baseConfiguration;

    public AppProfileService(IProfileStore<AppConfiguration> store, ProfileDescriptor baseProfile)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        BaseProfile = baseProfile ?? throw new ArgumentNullException(nameof(baseProfile));
        if (!BaseProfile.IsDefault)
        {
            BaseProfile = BaseProfile with { IsDefault = true };
        }
    }

    public ProfileDescriptor BaseProfile { get; }

    public ProfileDescriptor? ActiveProfile { get; private set; }

    public AppConfiguration? ActiveConfig { get; private set; }

    public event EventHandler<ProfileDescriptor>? ActiveProfileChanged;

    public Task<IReadOnlyList<ProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        _store.ListProfilesAsync(cancellationToken);

    public async Task<ProfileActivationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _baseConfiguration = await _store.LoadAsync(BaseProfile, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ProfileDescriptor> profiles = await _store.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
            ProfileDescriptor? selected = FindProfile(profiles, _baseConfiguration.DefaultProfile);
            if (selected is not null)
            {
                try
                {
                    return await ActivateLockedAsync(selected, persistAsDefault: false, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException exception)
                {
                    ProfileActivationResult fallback = await ActivateLockedAsync(BaseProfile, persistAsDefault: false, cancellationToken).ConfigureAwait(false);
                    return fallback with { Message = $"Profile '{selected.Name}' could not be loaded; the base configuration was used. {exception.Message}" };
                }
            }

            return await ActivateLockedAsync(BaseProfile, persistAsDefault: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ProfileActivationResult(false, ActiveProfile, ActiveConfig, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ActivateAsync(ProfileDescriptor profile, CancellationToken cancellationToken = default)
    {
        ProfileActivationResult result = await ActivateAsync(profile, persistAsDefault: true, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    public async Task<ProfileActivationResult> ActivateAsync(
        ProfileDescriptor profile,
        bool persistAsDefault,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_baseConfiguration is null)
            {
                _baseConfiguration = await _store.LoadAsync(BaseProfile, cancellationToken).ConfigureAwait(false);
            }

            return await ActivateLockedAsync(profile, persistAsDefault, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ProfileActivationResult(false, ActiveProfile, ActiveConfig, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationSaveResult> SaveActiveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ActiveProfile is null)
            {
                return new ConfigurationSaveResult(false, "No active profile is available.");
            }

            await _store.SaveAsync(ActiveProfile, configuration, cancellationToken).ConfigureAwait(false);
            if (ActiveProfile.IsDefault)
            {
                _baseConfiguration = configuration;
            }

            ActiveConfig = configuration;
            return new ConfigurationSaveResult(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ConfigurationSaveResult(false, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProfileActivationResult> ActivateLockedAsync(
        ProfileDescriptor profile,
        bool persistAsDefault,
        CancellationToken cancellationToken)
    {
        AppConfiguration configuration = await _store.LoadAsync(profile, cancellationToken).ConfigureAwait(false);
        if (persistAsDefault)
        {
            _baseConfiguration ??= await _store.LoadAsync(BaseProfile, cancellationToken).ConfigureAwait(false);
            _baseConfiguration.DefaultProfile = profile.IsDefault ? null : profile.Name;
            await _store.SaveAsync(BaseProfile, _baseConfiguration, cancellationToken).ConfigureAwait(false);
        }

        bool changed = ActiveProfile is null || !string.Equals(ActiveProfile.Path, profile.Path, StringComparison.OrdinalIgnoreCase);
        ActiveProfile = profile;
        ActiveConfig = configuration;
        if (changed)
        {
            ActiveProfileChanged?.Invoke(this, profile);
        }

        return new ProfileActivationResult(true, profile, configuration);
    }

    private static ProfileDescriptor? FindProfile(IEnumerable<ProfileDescriptor> profiles, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(profile.Path), name, StringComparison.OrdinalIgnoreCase));
    }
}
