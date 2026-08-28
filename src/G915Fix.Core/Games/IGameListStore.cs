namespace G915Fix.Core.Games;

public interface IGameListStore
{
    Task<IReadOnlySet<string>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlySet<string> executableNames, CancellationToken cancellationToken = default);
}
