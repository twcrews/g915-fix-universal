using System.Net;
using System.Net.Http.Headers;

internal sealed class DetectableGamesClient
{
    private readonly HttpClient _httpClient;
    private readonly int _maxAttempts;

    internal DetectableGamesClient(HttpClient httpClient, int maxAttempts)
    {
        _httpClient = httpClient;
        _maxAttempts = Math.Max(1, maxAttempts);
    }

    internal async Task<DetectableGamesDownload> DownloadAsync(
        HttpCacheMetadata? cache,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "");
            ApplyCacheHeaders(request, cache);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (Exception ex) when (IsRetryableException(ex, cancellationToken) && attempt < _maxAttempts)
            {
                await DelayBeforeRetryAsync(null, attempt, cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                response.Dispose();
                return DetectableGamesDownload.CreateNotModified(cache);
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return DetectableGamesDownload.WithContent(response, content, HttpCacheMetadata.FromResponse(response));
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }

            if (IsRetryable(response.StatusCode) && attempt < _maxAttempts)
            {
                TimeSpan? retryAfter = GetRetryAfter(response.Headers.RetryAfter);
                response.Dispose();
                await DelayBeforeRetryAsync(retryAfter, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
            }
        }
    }

    private static void ApplyCacheHeaders(HttpRequestMessage request, HttpCacheMetadata? cache)
    {
        if (cache is null) return;

        if (!string.IsNullOrWhiteSpace(cache.ETag) && EntityTagHeaderValue.TryParse(cache.ETag, out EntityTagHeaderValue? etag))
            request.Headers.IfNoneMatch.Add(etag);

        if (!string.IsNullOrWhiteSpace(cache.LastModified) && DateTimeOffset.TryParse(cache.LastModified, out DateTimeOffset lastModified))
            request.Headers.IfModifiedSince = lastModified;
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests ||
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private static bool IsRetryableException(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is HttpRequestException || exception is IOException || exception is TaskCanceledException);

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null) return null;
        if (retryAfter.Delta.HasValue) return retryAfter.Delta.Value;
        if (retryAfter.Date.HasValue)
        {
            TimeSpan delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static Task DelayBeforeRetryAsync(TimeSpan? retryAfter, int attempt, CancellationToken cancellationToken)
    {
        TimeSpan delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(5_000, 250 * Math.Pow(2, attempt - 1)));
        return Task.Delay(delay, cancellationToken);
    }
}

internal sealed class DetectableGamesDownload : IAsyncDisposable
{
    private readonly HttpResponseMessage? _response;

    private DetectableGamesDownload(
        HttpResponseMessage? response,
        Stream? content,
        HttpCacheMetadata? cacheMetadata,
        bool notModified)
    {
        _response = response;
        Content = content;
        CacheMetadata = cacheMetadata;
        NotModified = notModified;
    }

    internal Stream? Content { get; }
    internal HttpCacheMetadata? CacheMetadata { get; }
    internal bool NotModified { get; }

    internal static DetectableGamesDownload WithContent(
        HttpResponseMessage response,
        Stream content,
        HttpCacheMetadata? cacheMetadata) =>
        new(response, content, cacheMetadata, notModified: false);

    internal static DetectableGamesDownload CreateNotModified(HttpCacheMetadata? cacheMetadata) =>
        new(null, null, cacheMetadata, notModified: true);

    public async ValueTask DisposeAsync()
    {
        if (Content is not null) await Content.DisposeAsync();
        _response?.Dispose();
    }
}
