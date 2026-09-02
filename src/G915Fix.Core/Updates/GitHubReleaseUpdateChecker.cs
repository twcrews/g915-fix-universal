using System.Text.Json;

namespace G915Fix.Core.Updates;

/// <summary>
/// Checks the latest GitHub release for this project. The checker only reports
/// availability; it never downloads or installs an update.
/// </summary>
public sealed class GitHubReleaseUpdateChecker : IUpdateChecker
{
    public static readonly Uri DefaultLatestReleaseApi =
        new("https://api.github.com/repos/twcrews/g915-fix-universal/releases/latest");

    public static readonly Uri DefaultReleaseTagUriPrefix =
        new("https://github.com/twcrews/g915-fix-universal/releases/tag/");

    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseApi;
    private readonly Uri _releaseTagUriPrefix;
    private readonly TimeSpan _requestTimeout;

    public GitHubReleaseUpdateChecker(
        HttpClient httpClient,
        Uri? latestReleaseApi = null,
        Uri? releaseTagUriPrefix = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _latestReleaseApi = latestReleaseApi ?? DefaultLatestReleaseApi;
        _releaseTagUriPrefix = releaseTagUriPrefix ?? DefaultReleaseTagUriPrefix;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);

        if (!_latestReleaseApi.IsAbsoluteUri || !_releaseTagUriPrefix.IsAbsoluteUri)
        {
            throw new ArgumentException("Release endpoints must be absolute URIs.");
        }

        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseApi);
            request.Headers.UserAgent.ParseAdd("G915Fix-update-check");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed($"The update service returned HTTP {(int)response.StatusCode}.");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutSource.Token)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tagElement)
                || tagElement.ValueKind != JsonValueKind.String
                || !TryParseVersion(tagElement.GetString(), out Version? latestVersion))
            {
                return Failed("The update service returned an invalid release tag.");
            }

            if (latestVersion <= Normalize(currentVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestVersion);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                latestVersion,
                BuildReleaseUri(tagElement.GetString()!));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed("The update check timed out.");
        }
        catch (HttpRequestException)
        {
            return Failed("The update service could not be reached.");
        }
        catch (JsonException)
        {
            return Failed("The update service returned invalid data.");
        }
    }

    private Uri BuildReleaseUri(string tag) =>
        new(_releaseTagUriPrefix, Uri.EscapeDataString(tag));

    private static UpdateCheckResult Failed(string message) =>
        new(UpdateCheckStatus.Failed, Message: message);

    private static bool TryParseVersion(string? tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string normalizedTag = tag.Trim();
        if (normalizedTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTag = normalizedTag[1..];
        }

        if (!Version.TryParse(normalizedTag, out Version? parsedVersion))
        {
            return false;
        }

        version = Normalize(parsedVersion);
        return true;
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));
}
