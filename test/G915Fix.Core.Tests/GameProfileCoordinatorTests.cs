using G915Fix.Core.Configuration;
using G915Fix.Core.Games;
using G915Fix.Core.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class GameProfileCoordinatorTests
{
    [TestMethod]
    public async Task Coordinator_SwitchesForGameAndRestoresBaseWithoutPersistingSelection()
    {
        var baseProfile = new ProfileDescriptor("config", "config.json", true);
        var gamingProfile = new ProfileDescriptor("gaming", "gaming.json");
        var profiles = new FakeProfileService(baseProfile, gamingProfile);
        var monitor = new FakeGameProcessMonitor();
        await using var coordinator = new GameProfileCoordinator(monitor, profiles, new GameProfileConfiguration
        {
            AutoSwitchProfiles = true,
            DefaultGameProfile = "gaming"
        });

        var switched = new TaskCompletionSource<GameProfileSwitchStatus>();
        coordinator.StatusChanged += (_, status) =>
        {
            if (status.ActiveProfile == "gaming") switched.TrySetResult(status);
        };
        monitor.Start("game");
        GameProfileSwitchStatus switchStatus = await switched.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("gaming", switchStatus.ActiveProfile);
        Assert.AreEqual("gaming", profiles.ActiveProfile!.Name);
        Assert.IsFalse(profiles.LastActivationPersisted);

        var restored = new TaskCompletionSource<GameProfileSwitchStatus>();
        coordinator.StatusChanged += (_, status) =>
        {
            if (status.ActiveGame is null && profiles.ActiveProfile?.IsDefault == true) restored.TrySetResult(status);
        };
        monitor.Stop();
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("config", profiles.ActiveProfile!.Name);
        Assert.IsFalse(profiles.LastActivationPersisted);
    }

    private sealed class FakeGameProcessMonitor : IGameProcessMonitor
    {
        public GameProcess? RunningGame { get; private set; }
        public GameProcessMonitorStatus Status => GameProcessMonitorStatus.Active;
        public event EventHandler<GameProcess>? GameStarted;
        public event EventHandler? GameStopped;
        public void SetKnownGames(IReadOnlySet<string> executableNames) { }
        public void Start(string executable) { RunningGame = new GameProcess(executable); GameStarted?.Invoke(this, RunningGame); }
        public void Stop() { RunningGame = null; GameStopped?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeProfileService : IAppProfileService
    {
        private readonly IReadOnlyList<ProfileDescriptor> _profiles;

        public FakeProfileService(ProfileDescriptor baseProfile, ProfileDescriptor gamingProfile)
        {
            BaseProfile = baseProfile;
            _profiles = [baseProfile, gamingProfile];
            ActiveProfile = baseProfile;
            ActiveConfig = new AppConfiguration();
        }

        public ProfileDescriptor BaseProfile { get; }
        public ProfileDescriptor? ActiveProfile { get; private set; }
        public AppConfiguration? ActiveConfig { get; private set; }
        public bool LastActivationPersisted { get; private set; }
        public event EventHandler<ProfileDescriptor>? ActiveProfileChanged;
        public Task<IReadOnlyList<ProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default) => Task.FromResult(_profiles);
        public Task<ProfileActivationResult> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileActivationResult(true, ActiveProfile, ActiveConfig));
        public Task ActivateAsync(ProfileDescriptor profile, CancellationToken cancellationToken = default) =>
            ActivateAsync(profile, true, cancellationToken).ContinueWith(_ => { }, cancellationToken);
        public Task<ProfileActivationResult> ActivateAsync(ProfileDescriptor profile, bool persistAsDefault, CancellationToken cancellationToken = default)
        {
            ActiveProfile = profile;
            ActiveConfig = new AppConfiguration();
            LastActivationPersisted = persistAsDefault;
            ActiveProfileChanged?.Invoke(this, profile);
            return Task.FromResult(new ProfileActivationResult(true, profile, ActiveConfig));
        }
        public Task<ConfigurationSaveResult> SaveActiveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfigurationSaveResult(true));
    }
}
