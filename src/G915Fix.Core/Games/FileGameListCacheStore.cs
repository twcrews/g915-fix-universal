using System.Text.Json;

namespace G915Fix.Core.Games;

/// <summary>Stores game-list HTTP validators in a JSON sidecar file.</summary>
public sealed class FileGameListCacheStore : IGameListCacheStore
{
    private readonly string _path;

    public FileGameListCacheStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<GameListCacheMetadata?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using Stream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<GameListCacheMetadata>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A stale or corrupt cache must not prevent a fresh download.
            return null;
        }
    }

    public async Task SaveAsync(GameListCacheMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (Stream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
