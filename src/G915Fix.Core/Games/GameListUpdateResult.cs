namespace G915Fix.Core.Games;

/// <summary>The outcome of refreshing the known-game executable list.</summary>
public sealed record GameListUpdateResult(
    GameListUpdateStatus Status,
    IReadOnlySet<string> ExecutableNames,
    string? Message = null,
    bool WasNotModified = false)
{
    public int GameCount => ExecutableNames.Count;
}
