using System.Text.Json;
using G915Fix.Core.Configuration;

namespace G915Fix.Core.Profiles;

/// <summary>Portable JSON profile discovery and persistence rooted at a host-supplied directory.</summary>
public sealed class JsonProfileStore : IProfileStore<AppConfiguration>
{
    private static readonly string[] RecognizedProperties =
    [
        nameof(AppConfiguration.SchemaVersion), nameof(AppConfiguration.Keyboard),
        nameof(AppConfiguration.Mouse), nameof(AppConfiguration.Diagnostics),
        nameof(AppConfiguration.Updates), nameof(AppConfiguration.Games)
    ];

    private readonly string _directory;
    private readonly string _baseFileName;

    public JsonProfileStore(string directory, string baseFileName = "config.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        _directory = Path.GetFullPath(directory);
        _baseFileName = baseFileName;
    }

    public async Task<IReadOnlyList<ProfileDescriptor>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var profiles = new List<ProfileDescriptor>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsProfileAsync(path, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            profiles.Add(new ProfileDescriptor(
                Path.GetFileNameWithoutExtension(path),
                path,
                string.Equals(fileName, _baseFileName, StringComparison.OrdinalIgnoreCase)));
        }

        return profiles.OrderByDescending(profile => profile.IsDefault).ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<AppConfiguration> LoadAsync(ProfileDescriptor profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ConfigurationLoadResult result = await new JsonAppConfigurationStore(profile.Path).LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidDataException($"Could not load profile '{profile.Name}': {result.Error}");
        }

        return result.Configuration;
    }

    public async Task SaveAsync(ProfileDescriptor profile, AppConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ConfigurationSaveResult result = await new JsonAppConfigurationStore(profile.Path).SaveAsync(config, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new IOException($"Could not save profile '{profile.Name}': {result.Error}");
        }
    }

    private static async Task<bool> IsProfileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return document.RootElement.EnumerateObject().Any(property =>
                RecognizedProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
