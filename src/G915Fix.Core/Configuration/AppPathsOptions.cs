namespace G915Fix.Core.Configuration;

public sealed class AppPathsOptions
{
    public string AppDirectory { get; init; } = AppContext.BaseDirectory;

    public string ConfigDirectory { get; init; } = string.Empty;

    public string LogDirectory { get; init; } = string.Empty;
}
