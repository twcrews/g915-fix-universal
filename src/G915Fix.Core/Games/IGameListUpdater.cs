namespace G915Fix.Core.Games;

public interface IGameListUpdater
{
    Task<IReadOnlySet<string>> UpdateAsync(CancellationToken cancellationToken = default);
}
