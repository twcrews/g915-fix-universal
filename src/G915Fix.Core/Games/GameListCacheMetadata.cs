namespace G915Fix.Core.Games;

/// <summary>HTTP cache validators returned by the game-list source.</summary>
public sealed record GameListCacheMetadata(
    string? ETag = null,
    DateTimeOffset? LastModified = null);
