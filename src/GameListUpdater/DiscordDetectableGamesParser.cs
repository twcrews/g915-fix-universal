using System.Text;
using System.Text.Json;

internal static class DiscordDetectableGamesParser
{
    /// <summary>
    /// Generic runtime / dev hosts that appear in Discord's list but run many
    /// non-game apps; excluded so they don't false-trigger the game profile.
    /// </summary>
    private static readonly HashSet<string> Denylist = new(StringComparer.Ordinal)
    {
        "dotnet.exe", "python.exe", "pythonw.exe", "node.exe",
        "ruby.exe", "perl.exe", "mono.exe", "pwsh.exe"
    };

    internal static List<string> ExtractExecutableNames(string json, string osFilter)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        return ExtractExecutableNamesAsync(stream, osFilter).GetAwaiter().GetResult();
    }

    internal static async Task<List<string>> ExtractExecutableNamesAsync(
        Stream json,
        string osFilter,
        CancellationToken cancellationToken = default)
    {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string normalizedOsFilter = NormalizeOsFilter(osFilter);

        await foreach (JsonElement game in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(json, cancellationToken: cancellationToken))
        {
            if (game.ValueKind != JsonValueKind.Object ||
                !game.TryGetProperty("executables", out JsonElement executables) ||
                executables.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement executable in executables.EnumerateArray())
            {
                if (executable.ValueKind != JsonValueKind.Object) continue;
                if (!TryGetExecutableName(executable, normalizedOsFilter, out string name)) continue;

                name = NormalizeExecutableName(name);
                if (name.Length > 0 && !Denylist.Contains(name) && seen.Add(name)) result.Add(name);
            }
        }

        return result;
    }

    private static bool TryGetExecutableName(JsonElement executable, string osFilter, out string name)
    {
        name = string.Empty;

        if (!MatchesOs(GetStringProperty(executable, "os"), osFilter)) return false;

        string? executableName = GetStringProperty(executable, "name");
        if (string.IsNullOrEmpty(executableName)) return false;

        name = executableName;
        return true;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string NormalizeOsFilter(string osFilter) => osFilter.Trim().ToLowerInvariant();

    private static bool MatchesOs(string? executableOs, string osFilter) =>
        osFilter == "all"
            ? !string.IsNullOrWhiteSpace(executableOs)
            : string.Equals(executableOs, osFilter, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExecutableName(string name)
    {
        int slash = name.LastIndexOf('/');
        int backslash = name.LastIndexOf('\\');
        int separator = Math.Max(slash, backslash);
        if (separator >= 0) name = name[(separator + 1)..];

        return name.Trim().ToLowerInvariant();
    }
}
