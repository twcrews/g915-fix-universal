namespace G915Fix.Core.Games;

/// <summary>Stores HTTP validators for a game-list source.</summary>
public interface IGameListCacheStore
{
    Task<GameListCacheMetadata?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameListCacheMetadata metadata, CancellationToken cancellationToken = default);
}
