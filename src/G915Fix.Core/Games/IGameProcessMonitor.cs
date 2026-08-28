using G915Fix.Core.Abstractions;

namespace G915Fix.Core.Games;

public interface IGameProcessMonitor
{
    GameProcess? RunningGame { get; }

    event EventHandler<GameProcess>? GameStarted;

    event EventHandler? GameStopped;

    void SetKnownGames(IReadOnlySet<string> executableNames);
}
