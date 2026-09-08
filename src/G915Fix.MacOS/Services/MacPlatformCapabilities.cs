using G915Fix.Core.Games;
using G915Fix.Core.Input;

namespace G915Fix.MacOS.Services;

/// <summary>macOS has no UIPI-style foreground access boundary exposed to this host.</summary>
internal sealed class MacForegroundInputAccessDetector : IForegroundInputAccessDetector
{
    public ForegroundInputAccessResult GetCurrentStatus() => new(
        ForegroundInputAccessStatus.NotSupported,
        Message: "macOS does not expose a per-foreground-app filtering access check.");
}

/// <summary>
/// The first macOS host deliberately does not infer games from process names.
/// It reports this limitation instead of allowing a saved toggle to pretend to switch profiles.
/// </summary>
internal sealed class UnsupportedMacGameProcessMonitor : IGameProcessMonitor
{
    public GameProcess? RunningGame => null;
    public GameProcessMonitorStatus Status => GameProcessMonitorStatus.Unsupported;
    public event EventHandler<GameProcess>? GameStarted { add { } remove { } }
    public event EventHandler? GameStopped { add { } remove { } }
    public event EventHandler<GameProcessMonitorStatus>? StatusChanged { add { } remove { } }
    public void SetKnownGames(IReadOnlySet<string> executableNames) { }
}
