internal sealed record GameListUpdaterOptions(
    Uri ApiUri,
    string OutputPath,
    string? CachePath,
    string OsFilter,
    int TimeoutSeconds,
    int MaxAttempts,
    bool ShowHelp)
{
    internal const string DefaultOsFilter = "win32";
    private const int DefaultTimeoutSeconds = 45;
    private const int DefaultMaxAttempts = 3;
    private static readonly Uri DefaultApiUri = new("https://discord.com/api/v9/applications/detectable");

    internal static string Usage =>
        "Usage: GameListUpdater [--os win32|linux|darwin|all] [--output PATH] [--cache PATH|--no-cache] " +
        "[--timeout SECONDS] [--retries COUNT] [--api-url URL]";

    internal static bool TryParse(string[] args, out GameListUpdaterOptions options, out string? error)
    {
        Uri apiUri = DefaultApiUri;
        string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games.txt");
        string osFilter = DefaultOsFilter;
        int timeoutSeconds = DefaultTimeoutSeconds;
        int maxAttempts = DefaultMaxAttempts;
        bool showHelp = false;
        bool cacheDisabled = false;
        string? cachePath = null;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "--os":
                    if (!TryReadValue(args, ref i, arg, out osFilter, out error)) return Fail(out options);
                    osFilter = osFilter.Trim().ToLowerInvariant();
                    if (osFilter.Length == 0)
                    {
                        error = "--os cannot be empty.";
                        return Fail(out options);
                    }
                    break;

                case "--output":
                    if (!TryReadValue(args, ref i, arg, out outputPath, out error)) return Fail(out options);
                    outputPath = Path.GetFullPath(outputPath);
                    break;

                case "--cache":
                    if (!TryReadValue(args, ref i, arg, out string rawCachePath, out error)) return Fail(out options);
                    cachePath = Path.GetFullPath(rawCachePath);
                    cacheDisabled = false;
                    break;

                case "--no-cache":
                    cacheDisabled = true;
                    cachePath = null;
                    break;

                case "--timeout":
                    if (!TryReadInt(args, ref i, arg, out timeoutSeconds, out error)) return Fail(out options);
                    if (timeoutSeconds <= 0)
                    {
                        error = "--timeout must be greater than zero.";
                        return Fail(out options);
                    }
                    break;

                case "--retries":
                    if (!TryReadInt(args, ref i, arg, out maxAttempts, out error)) return Fail(out options);
                    if (maxAttempts <= 0)
                    {
                        error = "--retries must be greater than zero.";
                        return Fail(out options);
                    }
                    break;

                case "--api-url":
                    if (!TryReadValue(args, ref i, arg, out string apiUrl, out error)) return Fail(out options);
                    if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out Uri? parsedApiUri))
                    {
                        error = "--api-url must be an absolute URI.";
                        return Fail(out options);
                    }

                    apiUri = parsedApiUri;
                    break;

                default:
                    error = "Unknown argument: " + arg;
                    return Fail(out options);
            }
        }

        if (!cacheDisabled && cachePath is null) cachePath = outputPath + ".httpcache.json";

        options = new GameListUpdaterOptions(apiUri, outputPath, cachePath, osFilter, timeoutSeconds, maxAttempts, showHelp);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string name, out string value, out string? error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = name + " requires a value.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static bool TryReadInt(string[] args, ref int index, string name, out int value, out string? error)
    {
        if (!TryReadValue(args, ref index, name, out string raw, out error))
        {
            value = 0;
            return false;
        }

        if (int.TryParse(raw, out value)) return true;

        error = name + " must be an integer.";
        return false;
    }

    private static bool Fail(out GameListUpdaterOptions options)
    {
        options = new GameListUpdaterOptions(DefaultApiUri, string.Empty, null, DefaultOsFilter, DefaultTimeoutSeconds, DefaultMaxAttempts, false);
        return false;
    }
}
