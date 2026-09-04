using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace G915Fix.Core.Configuration;

/// <summary>
/// JSON configuration persistence built on the standard configuration binder for
/// reads and atomic replacement for writes.
/// </summary>
public sealed class JsonAppConfigurationStore : IAppConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonAppConfigurationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public Task<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path))
        {
            return Task.FromResult(new ConfigurationLoadResult(new AppConfiguration(), false));
        }

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(Path, optional: false, reloadOnChange: false)
                .Build();
            AppConfiguration bound = configuration.Get<AppConfiguration>() ?? new AppConfiguration();
            return Task.FromResult(new ConfigurationLoadResult(bound, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return Task.FromResult(new ConfigurationLoadResult(new AppConfiguration(), true, exception.Message));
        }
    }

    public async Task<ConfigurationSaveResult> SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        string temporaryPath = Path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, Path, overwrite: true);
            return new ConfigurationSaveResult(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            return new ConfigurationSaveResult(false, exception.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A failed cleanup must not hide the primary persistence result.
            }
        }
    }
}
