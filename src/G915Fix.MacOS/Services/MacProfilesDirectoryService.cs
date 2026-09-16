using System.Diagnostics;
using G915Fix.Desktop.Services;

namespace G915Fix.MacOS.Services;

/// <summary>Reveals the app's profile directory in Finder.</summary>
internal sealed class MacProfilesDirectoryService(string directory) : IProfilesDirectoryService
{
    private readonly string _directory = Path.GetFullPath(directory);

    public async Task<ProfilesDirectoryOpenResult> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return new ProfilesDirectoryOpenResult(false, "The profiles directory is unavailable.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/open")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { _directory }
            }) ?? throw new InvalidOperationException("macOS could not start Finder.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return new ProfilesDirectoryOpenResult(false, string.IsNullOrWhiteSpace(error)
                    ? "macOS could not open the profiles directory."
                    : error.Trim());
            }

            return new ProfilesDirectoryOpenResult(true, "Profiles directory opened in Finder.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ProfilesDirectoryOpenResult(false, $"Could not open the profiles directory: {exception.Message}");
        }
    }
}
