using System.Net;
using G915Fix.Core.Games;

return await GameListUpdaterCli.RunAsync(args);

internal static class GameListUpdaterCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!TryParse(args, out CliOptions? options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var store = new FileGameListStore(options.OutputPath, options.Platform);
        IGameListCacheStore? cacheStore = options.CachePath is null ? null : new FileGameListCacheStore(options.CachePath);
        var updater = new DiscordGameListUpdater(client, store, new GameListUpdateOptions
        {
            Platform = options.Platform,
            RequestTimeout = options.Timeout,
            MaxAttempts = options.MaxAttempts
        }, cacheStore);

        GameListUpdateResult result = await updater.UpdateAsync(cancellationToken);
        Console.WriteLine($"{result.Status.ToString().ToUpperInvariant()} games={result.GameCount}" +
            (string.IsNullOrWhiteSpace(result.Message) ? string.Empty : $" message={result.Message}"));
        return result.Status switch
        {
            GameListUpdateStatus.Updated => 0,
            GameListUpdateStatus.UpToDate => 1,
            _ => 2
        };
    }

    private const string Usage =
        "Usage: G915Fix.GameListUpdater [--os win32|linux|darwin|all] [--output PATH] " +
        "[--cache PATH|--no-cache] [--timeout SECONDS] [--retries COUNT]";

    private static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        string outputPath = Path.Combine(AppContext.BaseDirectory, "games.txt");
        string? cachePath = null;
        GameListPlatform platform = GameListUpdateOptions.GetCurrentPlatform();
        TimeSpan timeout = TimeSpan.FromSeconds(45);
        int maxAttempts = 3;
        bool showHelp = false;
        bool cacheDisabled = false;
        error = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--os":
                    if (!TryReadValue(args, ref index, argument, out string os, out error)
                        || !TryParsePlatform(os, out platform))
                    {
                        error ??= "--os must be win32, linux, darwin, or all.";
                        return Fail(out options);
                    }
                    break;
                case "--output":
                    if (!TryReadValue(args, ref index, argument, out outputPath, out error)) return Fail(out options);
                    outputPath = Path.GetFullPath(outputPath);
                    break;
                case "--cache":
                    if (!TryReadValue(args, ref index, argument, out string cache, out error)) return Fail(out options);
                    cachePath = Path.GetFullPath(cache);
                    cacheDisabled = false;
                    break;
                case "--no-cache":
                    cacheDisabled = true;
                    cachePath = null;
                    break;
                case "--timeout":
                    if (!TryReadValue(args, ref index, argument, out string timeoutSeconds, out error)
                        || !int.TryParse(timeoutSeconds, out int seconds) || seconds <= 0)
                    {
                        error ??= "--timeout must be a positive integer.";
                        return Fail(out options);
                    }
                    timeout = TimeSpan.FromSeconds(seconds);
                    break;
                case "--retries":
                    if (!TryReadValue(args, ref index, argument, out string retries, out error)
                        || !int.TryParse(retries, out maxAttempts) || maxAttempts <= 0)
                    {
                        error ??= "--retries must be a positive integer.";
                        return Fail(out options);
                    }
                    break;
                default:
                    error = "Unknown argument: " + argument;
                    return Fail(out options);
            }
        }

        outputPath = Path.GetFullPath(outputPath);
        if (!cacheDisabled && cachePath is null) cachePath = outputPath + ".httpcache.json";
        options = new CliOptions(outputPath, cachePath, platform, timeout, maxAttempts, showHelp);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string argument, out string value, out string? error)
    {
        if (++index >= args.Length)
        {
            value = string.Empty;
            error = argument + " requires a value.";
            return false;
        }

        value = args[index];
        error = null;
        return true;
    }

    private static bool TryParsePlatform(string value, out GameListPlatform platform)
    {
        platform = value.Trim().ToLowerInvariant() switch
        {
            "win32" => GameListPlatform.Windows,
            "darwin" => GameListPlatform.MacOS,
            "linux" => GameListPlatform.Linux,
            "all" => GameListPlatform.All,
            _ => (GameListPlatform)(-1)
        };
        return Enum.IsDefined(platform);
    }

    private static bool Fail(out CliOptions options)
    {
        options = null!;
        return false;
    }

    private sealed record CliOptions(
        string OutputPath,
        string? CachePath,
        GameListPlatform Platform,
        TimeSpan Timeout,
        int MaxAttempts,
        bool ShowHelp);
}
