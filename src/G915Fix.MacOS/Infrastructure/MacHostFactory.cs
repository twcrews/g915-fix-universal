using G915Fix.Core.Configuration;
using G915Fix.Core.Profiles;
using G915Fix.Core.Updates;
using G915Fix.Desktop.Services;
using G915Fix.Desktop.ViewModels;
using G915Fix.MacOS.Input;
using G915Fix.MacOS.Services;

namespace G915Fix.MacOS.Infrastructure;

internal static class MacHostFactory
{
    public static MacHost Create()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDirectory = Path.Combine(home, "Library", "Application Support", "G915Fix");
        string logDirectory = Path.Combine(home, "Library", "Logs", "G915Fix");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(logDirectory);

        string configPath = Path.Combine(configDirectory, "config.json");
        EnsureBaseConfiguration(configPath, Path.Combine(logDirectory, "filter-diagnostics.jsonl"));

        var profiles = new AppProfileService(
            new JsonProfileStore(configDirectory),
            new ProfileDescriptor("config", configPath, IsDefault: true));
        var permissions = new MacPermissionService();
        var diagnostics = new MacDiagnosticRouter();
        var runtime = new MacInputFilterRuntime(permissions, diagnostics);
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The host executable path is unavailable.");
        var autostart = new MacAutostartService(executable);
        var updates = new GitHubReleaseUpdateChecker(new HttpClient());
        var heatmaps = new MacHeatmapReportService(() => profiles.ActiveConfig?.Diagnostics?.LogPath);
        var services = new DesktopApplicationServices(runtime, profiles, permissions, autostart, updates, heatmapReports: heatmaps);
        var viewModel = new DesktopMainViewModel(
            services,
            new DesktopHostOptions(GetVersion(), "G915 Fix"));
        return new MacHost(viewModel, runtime);
    }

    private static void EnsureBaseConfiguration(string path, string diagnosticPath)
    {
        if (File.Exists(path))
        {
            return;
        }

        var configuration = new AppConfiguration
        {
            Diagnostics = new DiagnosticsConfiguration { LogPath = diagnosticPath }
        };
        ConfigurationSaveResult result = new JsonAppConfigurationStore(path).SaveAsync(configuration).GetAwaiter().GetResult();
        if (!result.Succeeded)
        {
            throw new IOException($"Could not create the default configuration: {result.Error}");
        }
    }

    private static Version GetVersion() =>
        typeof(MacHostFactory).Assembly.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : new Version(0, 1, 0);
}

internal sealed class MacHost(DesktopMainViewModel viewModel, MacInputFilterRuntime runtime) : IDisposable
{
    public DesktopMainViewModel ViewModel { get; } = viewModel;

    public void Dispose()
    {
        ViewModel.Dispose();
        runtime.Dispose();
    }
}
