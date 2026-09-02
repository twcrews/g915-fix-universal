namespace G915Fix.Core.Games;

public interface IGameListUpdater
{
    Task<GameListUpdateResult> UpdateAsync(CancellationToken cancellationToken = default);
}
