using System.Diagnostics;
using System.Security;
using G915Fix.Core.Autostart;

namespace G915Fix.MacOS.Services;

/// <summary>Owns a single per-user launchd agent; it never edits a foreign registration.</summary>
internal sealed class MacAutostartService : IAutostartService
{
    private const string Label = "com.twcrews.g915fix";
    private const string OwnershipKey = "G915FixManaged";
    private readonly string _agentPath;
    private readonly string _executablePath;

    public MacAutostartService(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _agentPath = Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
    }

    public Task<AutostartRegistration> GetRegistrationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_agentPath))
        {
            return Task.FromResult(new AutostartRegistration(AutostartStatus.Disabled, "Start at login is disabled."));
        }

        try
        {
            string plist = File.ReadAllText(_agentPath);
            return Task.FromResult(IsOwned(plist)
                ? new AutostartRegistration(AutostartStatus.Enabled, "G915 Fix starts at login.")
                : new AutostartRegistration(AutostartStatus.Conflict, "A launch agent with G915 Fix's identifier is not owned by this app."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new AutostartRegistration(AutostartStatus.Unknown, exception.Message));
        }
    }

    public async Task<AutostartRegistration> EnableAsync(CancellationToken cancellationToken = default)
    {
        AutostartRegistration current = await GetRegistrationAsync(cancellationToken).ConfigureAwait(false);
        if (current.Status is AutostartStatus.Conflict or AutostartStatus.Enabled)
        {
            return current;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_agentPath)!);
            await File.WriteAllTextAsync(_agentPath, BuildPlist(), cancellationToken).ConfigureAwait(false);
            await LaunchCtlAsync("bootstrap", $"gui/{GetUserId()}", _agentPath, cancellationToken).ConfigureAwait(false);
            return new AutostartRegistration(AutostartStatus.Enabled, "G915 Fix will start at your next login.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AutostartRegistration(AutostartStatus.RequiresUserAction, exception.Message);
        }
    }

    public async Task<AutostartRegistration> DisableAsync(CancellationToken cancellationToken = default)
    {
        AutostartRegistration current = await GetRegistrationAsync(cancellationToken).ConfigureAwait(false);
        if (current.Status != AutostartStatus.Enabled)
        {
            return current;
        }

        try
        {
            await LaunchCtlAsync("bootout", $"gui/{GetUserId()}", _agentPath, cancellationToken, tolerateFailure: true).ConfigureAwait(false);
            File.Delete(_agentPath);
            return new AutostartRegistration(AutostartStatus.Disabled, "Start at login is disabled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AutostartRegistration(AutostartStatus.RequiresUserAction, exception.Message);
        }
    }

    private string BuildPlist()
    {
        string executable = SecurityElement.Escape(_executablePath) ?? throw new InvalidOperationException("The executable path cannot be represented in a launchd plist.");
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>{{Label}}</string>
              <key>{{OwnershipKey}}</key><true/>
              <key>ProgramArguments</key><array><string>{{executable}}</string></array>
              <key>RunAtLoad</key><true/>
              <key>ProcessType</key><string>Interactive</string>
            </dict></plist>
            """;
    }

    private static bool IsOwned(string plist) => plist.Contains($"<key>{OwnershipKey}</key><true/>", StringComparison.Ordinal);

    private static async Task LaunchCtlAsync(string command, string domain, string path, CancellationToken cancellationToken, bool tolerateFailure = false)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/launchctl")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList = { command, domain, path }
        }) ?? throw new InvalidOperationException("launchctl could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 && !tolerateFailure)
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "launchctl rejected the login item." : error.Trim());
        }
    }

    private static uint GetUserId() => unchecked((uint)getuid());

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint getuid();
}
