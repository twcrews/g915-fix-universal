namespace G915Fix.Core.Games;

/// <summary>Configures a Discord detectable-games list refresh.</summary>
public sealed class GameListUpdateOptions
{
    public static readonly Uri DefaultApiUri =
        new("https://discord.com/api/v9/applications/detectable");

    public Uri ApiUri { get; init; } = DefaultApiUri;

    public GameListPlatform Platform { get; init; } = GetCurrentPlatform();

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public int MaxAttempts { get; init; } = 3;

    public static GameListPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return GameListPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return GameListPlatform.MacOS;
        if (OperatingSystem.IsLinux()) return GameListPlatform.Linux;
        throw new PlatformNotSupportedException("Discord game-list filtering is unsupported on this platform.");
    }

    public static string ToDiscordOsFilter(GameListPlatform platform) => platform switch
    {
        GameListPlatform.Windows => "win32",
        GameListPlatform.MacOS => "darwin",
        GameListPlatform.Linux => "linux",
        GameListPlatform.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };
}
