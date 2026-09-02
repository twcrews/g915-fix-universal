using System.Text.Json;

namespace G915Fix.Core.Games;

/// <summary>Extracts executable names from Discord's detectable-games response.</summary>
public static class DiscordDetectableGamesParser
{
    private static readonly HashSet<string> Denylist = new(StringComparer.Ordinal)
    {
        "dotnet", "dotnet.exe", "python", "python.exe", "pythonw.exe",
        "node", "node.exe", "ruby", "ruby.exe", "perl", "perl.exe",
        "mono", "mono.exe", "pwsh", "pwsh.exe"
    };

    public static async Task<IReadOnlySet<string>> ExtractExecutableNamesAsync(
        Stream json,
        GameListPlatform platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        string osFilter = GameListUpdateOptions.ToDiscordOsFilter(platform);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (JsonElement game in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
            json,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (game.ValueKind != JsonValueKind.Object
                || !game.TryGetProperty("executables", out JsonElement executables)
                || executables.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement executable in executables.EnumerateArray())
            {
                if (executable.ValueKind != JsonValueKind.Object
                    || !MatchesPlatform(executable, osFilter)
                    || !executable.TryGetProperty("name", out JsonElement nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string name = NormalizeExecutableName(nameElement.GetString());
                if (name.Length > 0 && !Denylist.Contains(name))
                {
                    result.Add(name);
                }
            }
        }

        return result;
    }

    private static bool MatchesPlatform(JsonElement executable, string osFilter)
    {
        if (!executable.TryGetProperty("os", out JsonElement osElement)
            || osElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return osFilter == "all"
            ? !string.IsNullOrWhiteSpace(osElement.GetString())
            : string.Equals(osElement.GetString(), osFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExecutableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        int separator = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        return name[(separator + 1)..].Trim().ToLowerInvariant();
    }
}
