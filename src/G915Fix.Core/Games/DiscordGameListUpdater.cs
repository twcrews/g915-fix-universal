using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace G915Fix.Core.Games;

/// <summary>
/// Refreshes a stored game list from Discord's public detectable-games endpoint.
/// This service is portable and performs no process monitoring or UI work.
/// </summary>
public sealed class DiscordGameListUpdater : IGameListUpdater
{
    private readonly HttpClient _httpClient;
    private readonly IGameListStore _gameListStore;
    private readonly IGameListCacheStore? _cacheStore;
    private readonly GameListUpdateOptions _options;

    public DiscordGameListUpdater(
        HttpClient httpClient,
        IGameListStore gameListStore,
        GameListUpdateOptions? options = null,
        IGameListCacheStore? cacheStore = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(gameListStore);

        _httpClient = httpClient;
        _gameListStore = gameListStore;
        _cacheStore = cacheStore;
        _options = options ?? new GameListUpdateOptions();
        ValidateOptions(_options);
    }

    public async Task<GameListUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> currentGames;
        try
        {
            currentGames = await _gameListStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            // A 304 response is only useful when a previously persisted body is
            // available. Empty lists are never saved from a successful response.
            GameListCacheMetadata? cache = _cacheStore is null || currentGames.Count == 0
                ? null
                : await _cacheStore.LoadAsync(cancellationToken).ConfigureAwait(false);

            for (int attempt = 1; attempt <= _options.MaxAttempts; attempt++)
            {
                using var request = CreateRequest(cache);
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(_options.RequestTimeout);

                try
                {
                    using HttpResponseMessage response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutSource.Token).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotModified)
                    {
                        return new GameListUpdateResult(
                            GameListUpdateStatus.UpToDate,
                            currentGames,
                            "The game list has not changed.",
                            WasNotModified: true);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        if (IsRetryable(response.StatusCode) && attempt < _options.MaxAttempts)
                        {
                            await DelayBeforeRetryAsync(response.Headers.RetryAfter, attempt, cancellationToken)
                                .ConfigureAwait(false);
                            continue;
                        }

                        return Failed(currentGames, $"The game-list service returned HTTP {(int)response.StatusCode}.");
                    }

                    await using Stream content = await response.Content.ReadAsStreamAsync(timeoutSource.Token)
                        .ConfigureAwait(false);
                    IReadOnlySet<string> downloadedGames = await DiscordDetectableGamesParser.ExtractExecutableNamesAsync(
                        content,
                        _options.Platform,
                        timeoutSource.Token).ConfigureAwait(false);
                    if (downloadedGames.Count == 0)
                    {
                        return Failed(currentGames, "The game-list service returned no usable executables.");
                    }

                    bool unchanged = SetEquals(currentGames, downloadedGames);
                    if (!unchanged)
                    {
                        await _gameListStore.SaveAsync(downloadedGames, cancellationToken).ConfigureAwait(false);
                    }

                    if (_cacheStore is not null)
                    {
                        try
                        {
                            await _cacheStore.SaveAsync(GetCacheMetadata(response), cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            return new GameListUpdateResult(
                                unchanged ? GameListUpdateStatus.UpToDate : GameListUpdateStatus.Updated,
                                downloadedGames,
                                "The game list was refreshed, but its HTTP cache could not be saved.");
                        }
                    }

                    return new GameListUpdateResult(
                        unchanged ? GameListUpdateStatus.UpToDate : GameListUpdateStatus.Updated,
                        downloadedGames,
                        unchanged ? "The game list is already current." : null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (attempt < _options.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(null, attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < _options.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(null, attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Failed(currentGames, "The game-list request timed out.");
                }
                catch (HttpRequestException)
                {
                    return Failed(currentGames, "The game-list service could not be reached.");
                }
                catch (JsonException)
                {
                    return Failed(currentGames, "The game-list service returned invalid data.");
                }
            }

            return Failed(currentGames, "The game-list refresh could not be completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(new HashSet<string>(StringComparer.OrdinalIgnoreCase), "The existing game list could not be read or saved.");
        }
    }

    private HttpRequestMessage CreateRequest(GameListCacheMetadata? cache)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.ApiUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("g915-fix-universal-GameListUpdater", "1.0"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/twcrews/g915-fix-universal)"));
        if (!string.IsNullOrWhiteSpace(cache?.ETag)
            && EntityTagHeaderValue.TryParse(cache.ETag, out EntityTagHeaderValue? etag))
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        if (cache?.LastModified is DateTimeOffset lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        return request;
    }

    private static GameListCacheMetadata GetCacheMetadata(HttpResponseMessage response) => new(
        response.Headers.ETag?.ToString(),
        response.Content.Headers.LastModified);

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    private static async Task DelayBeforeRetryAsync(
        RetryConditionHeaderValue? retryAfter,
        int attempt,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = retryAfter?.Delta
            ?? (retryAfter?.Date is DateTimeOffset date && date > DateTimeOffset.UtcNow
                ? date - DateTimeOffset.UtcNow
                : TimeSpan.FromMilliseconds(Math.Min(5_000, 250 * Math.Pow(2, attempt - 1))));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool SetEquals(IReadOnlySet<string> first, IReadOnlySet<string> second) =>
        first.Count == second.Count && first.All(second.Contains);

    private static GameListUpdateResult Failed(IReadOnlySet<string> games, string message) =>
        new(GameListUpdateStatus.Failed, games, message);

    private static void ValidateOptions(GameListUpdateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.ApiUri);
        if (!options.ApiUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The game-list API URI must be absolute.", nameof(options));
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RequestTimeout));
        }

        if (options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxAttempts));
        }
    }
}
