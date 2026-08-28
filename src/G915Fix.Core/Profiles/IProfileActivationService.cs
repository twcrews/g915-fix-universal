namespace G915Fix.Core.Profiles;

public interface IProfileActivationService<TConfig>
{
    ProfileDescriptor? ActiveProfile { get; }

    TConfig? ActiveConfig { get; }

    event EventHandler<ProfileDescriptor>? ActiveProfileChanged;

    Task ActivateAsync(ProfileDescriptor profile, CancellationToken cancellationToken = default);
}
