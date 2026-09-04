using G915Fix.Core.Configuration;
using G915Fix.Core.Profiles;

namespace G915Fix.Core.Games;

/// <summary>
/// Applies profile-switching policy to platform-provided game process events.
/// It contains no process-enumeration or platform assumptions.
/// </summary>
public sealed class GameProfileCoordinator : IAsyncDisposable
{
    private readonly IGameProcessMonitor _monitor;
    private readonly IAppProfileService _profiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameProfileConfiguration _configuration;
    private bool _switchedForGame;
    private bool _manualOverride;
    private int _disposed;

    public GameProfileCoordinator(
        IGameProcessMonitor monitor,
        IAppProfileService profiles,
        GameProfileConfiguration? configuration = null)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _configuration = configuration ?? new GameProfileConfiguration();
        Current = new GameProfileSwitchStatus(_configuration.AutoSwitchProfiles, _monitor.Status);
        _monitor.GameStarted += OnGameStarted;
        _monitor.GameStopped += OnGameStopped;
        _monitor.StatusChanged += OnMonitorStatusChanged;
    }

    public GameProfileSwitchStatus Current { get; private set; }

    public event EventHandler<GameProfileSwitchStatus>? StatusChanged;

    public void UpdateConfiguration(GameProfileConfiguration? configuration)
    {
        _configuration = configuration ?? new GameProfileConfiguration();
        Publish(Current with { Enabled = _configuration.AutoSwitchProfiles, MonitorStatus = _monitor.Status });
    }

    /// <summary>
    /// Activates a profile without changing the saved startup selection. While a
    /// game is running, the selection lasts until that game exits.
    /// </summary>
    public async Task<ProfileActivationResult> ActivateManualAsync(
        ProfileDescriptor profile,
        CancellationToken cancellationToken = default)
    {
        ProfileActivationResult result = await _profiles.ActivateAsync(profile, persistAsDefault: false, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded && _monitor.RunningGame is not null)
        {
            _manualOverride = true;
            _switchedForGame = false;
            Publish(Current with
            {
                ActiveGame = _monitor.RunningGame,
                ActiveProfile = profile.Name,
                IsManualOverride = true,
                Message = null
            });
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _monitor.GameStarted -= OnGameStarted;
        _monitor.GameStopped -= OnGameStopped;
        _monitor.StatusChanged -= OnMonitorStatusChanged;
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
    }

    private void OnGameStarted(object? sender, GameProcess game) => _ = HandleGameStartedAsync(game);

    private void OnGameStopped(object? sender, EventArgs args) => _ = HandleGameStoppedAsync();

    private void OnMonitorStatusChanged(object? sender, GameProcessMonitorStatus status) =>
        Publish(Current with { MonitorStatus = status });

    private async Task HandleGameStartedAsync(GameProcess game)
    {
        if (!_configuration.AutoSwitchProfiles || Volatile.Read(ref _disposed) != 0)
        {
            Publish(Current with { ActiveGame = game, Enabled = _configuration.AutoSwitchProfiles, MonitorStatus = _monitor.Status });
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _manualOverride = false;
            _switchedForGame = false;
            string? profileName = FindProfileName(game.ExecutableName);
            if (string.IsNullOrWhiteSpace(profileName))
            {
                Publish(new GameProfileSwitchStatus(true, _monitor.Status, game, Message: "No game profile is configured."));
                return;
            }

            IReadOnlyList<ProfileDescriptor> profiles = await _profiles.ListProfilesAsync().ConfigureAwait(false);
            ProfileDescriptor? profile = profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                Publish(new GameProfileSwitchStatus(true, _monitor.Status, game, Message: $"The configured profile '{profileName}' was not found."));
                return;
            }

            ProfileActivationResult result = await _profiles.ActivateAsync(profile, persistAsDefault: false).ConfigureAwait(false);
            _switchedForGame = result.Succeeded;
            Publish(new GameProfileSwitchStatus(
                true,
                _monitor.Status,
                game,
                result.Succeeded ? profile.Name : null,
                Message: result.Message));
        }
        catch (Exception exception)
        {
            Publish(new GameProfileSwitchStatus(true, _monitor.Status, game, Message: exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleGameStoppedAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_switchedForGame || _manualOverride)
            {
                ProfileActivationResult result = await _profiles.ActivateAsync(_profiles.BaseProfile, persistAsDefault: false).ConfigureAwait(false);
                Publish(new GameProfileSwitchStatus(
                    _configuration.AutoSwitchProfiles,
                    _monitor.Status,
                    Message: result.Succeeded ? null : result.Message));
            }
            else
            {
                Publish(new GameProfileSwitchStatus(_configuration.AutoSwitchProfiles, _monitor.Status));
            }

            _switchedForGame = false;
            _manualOverride = false;
        }
        catch (Exception exception)
        {
            Publish(new GameProfileSwitchStatus(_configuration.AutoSwitchProfiles, _monitor.Status, Message: exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? FindProfileName(string executableName)
    {
        string normalized = Path.GetFileName(executableName);
        foreach ((string executable, string profile) in _configuration.ProfileMap ?? [])
        {
            if (string.Equals(Path.GetFileName(executable), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return _configuration.DefaultGameProfile;
    }

    private void Publish(GameProfileSwitchStatus status)
    {
        if (Equals(Current, status))
        {
            return;
        }

        Current = status;
        StatusChanged?.Invoke(this, status);
    }
}

public sealed record GameProfileSwitchStatus(
    bool Enabled,
    GameProcessMonitorStatus MonitorStatus,
    GameProcess? ActiveGame = null,
    string? ActiveProfile = null,
    bool IsManualOverride = false,
    string? Message = null);
