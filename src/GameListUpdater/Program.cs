using System.Net;
using System.Net.Http.Headers;

// Single-purpose, network-isolated companion to KeyboardRepeatFilter.exe.
return await GameListUpdaterApp.RunAsync(args);

/// <summary>
/// Single-purpose, network-isolated companion to KeyboardRepeatFilter.exe.<br/>
/// Downloads Discord's public detectable-games database and writes games.txt.<br/>
/// Exit codes (read by the tray app): 0 = updated, 1 = no update, 2 = error.
/// </summary>
sealed class GameListUpdaterApp
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!GameListUpdaterOptions.TryParse(args, out GameListUpdaterOptions options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(GameListUpdaterOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(GameListUpdaterOptions.Usage);
            return 0;
        }

        try
        {
            using HttpClient httpClient = CreateHttpClient(options);
            DetectableGamesClient client = new(httpClient, options.MaxAttempts);
            HttpCacheMetadata? oldCache = File.Exists(options.OutputPath)
                ? await HttpCacheMetadataStore.ReadAsync(options.CachePath, cancellationToken)
                : null;

            await using DetectableGamesDownload download = await client.DownloadAsync(oldCache, cancellationToken);
            if (download.NotModified)
            {
                int cachedGames = File.Exists(options.OutputPath)
                    ? GameListWriter.CountBodyLines(await File.ReadAllTextAsync(options.OutputPath, cancellationToken))
                    : 0;

                Console.WriteLine("NO_UPDATE games=" + cachedGames + " cache=not-modified");
                return 1;
            }

            if (download.Content is null)
            {
                Console.Error.WriteLine("ERROR no response content");
                return 2;
            }

            List<string> exes = await DiscordDetectableGamesParser.ExtractExecutableNamesAsync(
                download.Content,
                options.OsFilter,
                cancellationToken);

            if (exes.Count == 0)
            {
                Console.Error.WriteLine("ERROR no executables parsed from response");
                return 2;
            }

            exes.Sort(StringComparer.Ordinal);

            string newContent = GameListWriter.BuildContent(exes, options.OsFilter);
            string? oldContent = File.Exists(options.OutputPath)
                ? await File.ReadAllTextAsync(options.OutputPath, cancellationToken)
                : null;

            bool sameBody = oldContent != null && GameListWriter.NormalizeBody(oldContent) == GameListWriter.NormalizeBody(newContent);
            if (!sameBody) GameListWriter.WriteAtomic(options.OutputPath, newContent);

            try
            {
                await HttpCacheMetadataStore.WriteAsync(options.CachePath, download.CacheMetadata, cancellationToken);
            }
            catch (Exception cacheEx) when (cacheEx is not OperationCanceledException)
            {
                Console.Error.WriteLine("WARN cache not saved: " + cacheEx.Message);
            }

            Console.WriteLine((sameBody ? "NO_UPDATE" : "UPDATED") + " games=" + exes.Count + " os=" + options.OsFilter);
            return sameBody ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine("ERROR " + ex.Message);
            return 2;
        }
    }

    private static HttpClient CreateHttpClient(GameListUpdaterOptions options)
    {
        HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        };

        HttpClient httpClient = new(handler)
        {
            BaseAddress = options.ApiUri,
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("g915-fix-universal-GameListUpdater", "1.0"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/twcrews/g915-fix-universal)"));
        return httpClient;
    }
}
