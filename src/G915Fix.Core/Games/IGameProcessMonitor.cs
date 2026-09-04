using G915Fix.Core.Abstractions;

namespace G915Fix.Core.Games;

public interface IGameProcessMonitor
{
    GameProcess? RunningGame { get; }

    /// <summary>Reports whether this platform can currently monitor game processes.</summary>
    GameProcessMonitorStatus Status => GameProcessMonitorStatus.Unsupported;

    event EventHandler<GameProcess>? GameStarted;

    event EventHandler? GameStopped;

    event EventHandler<GameProcessMonitorStatus>? StatusChanged
    {
        add { }
        remove { }
    }

    void SetKnownGames(IReadOnlySet<string> executableNames);
}

public enum GameProcessMonitorStatus
{
    Active,
    PermissionRequired,
    Unsupported,
    Faulted
}
