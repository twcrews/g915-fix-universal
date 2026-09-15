using System.Collections.ObjectModel;
using System.Windows.Input;
using G915Fix.Core.Autostart;
using G915Fix.Core.Configuration;
using G915Fix.Core.Input;
using G915Fix.Core.Games;
using G915Fix.Core.Notifications;
using G915Fix.Core.Permissions;
using G915Fix.Core.Heatmap;
using G915Fix.Core.Profiles;
using G915Fix.Core.Updates;
using G915Fix.Desktop.Infrastructure;
using G915Fix.Desktop.Services;

namespace G915Fix.Desktop.ViewModels;

/// <summary>
/// Shared shell and settings view model. It contains no native input, filesystem,
/// or OS registration logic; those operations are delegated to host services.
/// </summary>
public sealed class DesktopMainViewModel : ObservableObject, IDisposable
{
    private readonly DesktopApplicationServices _services;
    private readonly DesktopHostOptions _hostOptions;
    private readonly ConfigurationCompiler _configurationCompiler;
    private readonly SynchronizationContext? _synchronizationContext;
    private AppConfiguration _configuration = new();
    private ProfileDescriptor? _selectedProfile;
    private bool _suppressProfileActivation;
    private bool _configurationUpdateQueued;
    private int _configurationRevision;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private InputFilterRuntimeSnapshot _runtime = InputFilterRuntimeSnapshot.Inactive;
    private AutostartRegistration? _autostart;
    private bool _autostartEnabled;
    private UpdateCheckResult? _updateResult;
    private string? _message;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _canToggleInputFiltering;

    public DesktopMainViewModel(
        DesktopApplicationServices services,
        DesktopHostOptions hostOptions,
        ConfigurationCompiler? configurationCompiler = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _configurationCompiler = configurationCompiler ?? new ConfigurationCompiler();
        _synchronizationContext = SynchronizationContext.Current;
        _runtime = services.InputRuntime.Current;

        ActivateProfileCommand = new AsyncCommand(ActivateSelectedProfileAsync, () => !IsBusy && SelectedProfile is not null);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync, () => !IsBusy && _services.UpdateChecker is not null);
        OpenPermissionsCommand = new AsyncCommand(OpenPermissionsAsync);
        OpenHeatmapCommand = new AsyncCommand(OpenHeatmapAsync, () => !IsBusy && _services.HeatmapReports is not null);
        UpdateGamesListCommand = new AsyncCommand(UpdateGamesListAsync, () => !IsBusy && _services.GameListUpdater is not null);

        _services.InputRuntime.StatusChanged += OnRuntimeStatusChanged;
    }

    public string ApplicationName => _hostOptions.ApplicationName;
    public string VersionText => _hostOptions.CurrentVersion.ToString();
    public IReadOnlyList<string> KeyboardModes { get; } = Enum.GetNames<KeyboardDebounceMode>();
    public ObservableCollection<ProfileDescriptor> Profiles { get; } = [];
    public ObservableCollection<ConfigurationWarning> ConfigurationWarnings { get; } = [];

    public ICommand ActivateProfileCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenPermissionsCommand { get; }
    public ICommand OpenHeatmapCommand { get; }
    public ICommand UpdateGamesListCommand { get; }

    /// <summary>Raised when the host should show its platform-specific permissions UI.</summary>
    public event EventHandler? PermissionsWindowRequested;

    public bool IsInitialized
    {
        get => _isInitialized;
        private set
        {
            if (SetProperty(ref _isInitialized, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanToggleAutostart));
                RefreshCommands();
            }
        }
    }

    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public InputFilterRuntimeSnapshot Runtime { get => _runtime; private set => SetProperty(ref _runtime, value); }
    public AutostartRegistration? Autostart
    {
        get => _autostart;
        private set
        {
            if (SetProperty(ref _autostart, value))
            {
                SetProperty(ref _autostartEnabled, value?.IsEnabled == true, nameof(AutostartEnabled));
                OnPropertyChanged(nameof(CanToggleAutostart));
            }
        }
    }
    public UpdateCheckResult? UpdateResult { get => _updateResult; private set => SetProperty(ref _updateResult, value); }

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            if (!CanToggleAutostart)
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _autostartEnabled, value))
            {
                _ = SetAutostartEnabledAsync(value);
            }
        }
    }

    public bool CanToggleAutostart => !IsBusy && Autostart?.Status is AutostartStatus.Enabled or AutostartStatus.Disabled;

    public ProfileDescriptor? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RefreshCommands();
                if (!_suppressProfileActivation && value is not null)
                {
                    _ = ActivateSelectedProfileAsync();
                }
            }
        }
    }

    /// <summary>Whether every platform permission needed to filter input is currently granted.</summary>
    public bool CanToggleInputFiltering
    {
        get => _canToggleInputFiltering;
        private set => SetProperty(ref _canToggleInputFiltering, value);
    }

    /// <summary>Whether platform permission currently allows diagnostic input capture.</summary>
    public bool CanToggleDiagnostics => CanToggleInputFiltering;

    public bool KeyboardEnabled
    {
        get => _configuration.Keyboard.Enabled;
        set
        {
            if (!CanToggleInputFiltering && value)
            {
                OnPropertyChanged();
                return;
            }

            _configuration.Keyboard.Enabled = value;
            OnPropertyChanged();
            QueueConfigurationUpdate();
        }
    }

    public bool MouseEnabled
    {
        get => _configuration.Mouse.Enabled;
        set
        {
            if (!CanToggleInputFiltering && value)
            {
                OnPropertyChanged();
                return;
            }

            _configuration.Mouse.Enabled = value;
            OnPropertyChanged();
            QueueConfigurationUpdate();
        }
    }

    public string KeyboardMode
    {
        get => _configuration.Keyboard.Mode;
        set { _configuration.Keyboard.Mode = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    public double KeyboardMinimumRepeatIntervalMs
    {
        get => _configuration.Keyboard.MinimumRepeatIntervalMs;
        set { _configuration.Keyboard.MinimumRepeatIntervalMs = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    public double MouseMinimumRepeatIntervalMs
    {
        get => _configuration.Mouse.MinimumRepeatIntervalMs;
        set { _configuration.Mouse.MinimumRepeatIntervalMs = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    public bool DiagnosticsEnabled
    {
        get => _configuration.Diagnostics.Enabled;
        set
        {
            if (!CanToggleDiagnostics && value)
            {
                OnPropertyChanged();
                return;
            }

            _configuration.Diagnostics.Enabled = value;
            OnPropertyChanged();
            QueueConfigurationUpdate();
        }
    }

    public bool AutoSwitchProfiles
    {
        get => _configuration.Games.AutoSwitchProfiles;
        set { _configuration.Games.AutoSwitchProfiles = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    public bool CheckForUpdates
    {
        get => _configuration.Updates.CheckForUpdates;
        set { _configuration.Updates.CheckForUpdates = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    /// <summary>
    /// Loads the persisted startup profile and host state. Hosts call this during
    /// launch; filtering then follows the loaded keyboard and mouse toggles.
    /// </summary>
    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            ProfileActivationResult activation = await _services.Profiles.InitializeAsync();
            SetConfiguration(activation.ActiveConfiguration ?? new AppConfiguration());
            IsInitialized = activation.Succeeded;
            await RefreshHostStateAsync(activation.ActiveProfile);
            if (!activation.Succeeded)
            {
                Message = activation.Message ?? "Configuration could not be loaded.";
                return;
            }

            await RefreshInputFilteringPermissionAsyncCore();
            await ApplyFilterConfigurationAsync();
            Message = activation.Message ?? Runtime.Message ?? "Configuration loaded.";
        });
    }

    public async Task ActivateSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            ProfileActivationResult activation = await _services.Profiles.ActivateAsync(SelectedProfile, persistAsDefault: true);
            if (!activation.Succeeded || activation.ActiveConfiguration is null)
            {
                Message = activation.Message ?? "Could not activate profile.";
                return;
            }

            SetConfiguration(activation.ActiveConfiguration);
            SetSelectedProfileWithoutActivation(activation.ActiveProfile);
            await RefreshInputFilteringPermissionAsyncCore();
            await ApplyFilterConfigurationAsync();
            Message = activation.Message ?? Runtime.Message ?? $"Activated {SelectedProfile?.Name}.";
        });
    }

    private async Task SetAutostartEnabledAsync(bool enabled)
    {
        await RunAsync(async () =>
        {
            Autostart = enabled
                ? await _services.Autostart.EnableAsync()
                : await _services.Autostart.DisableAsync();
            Message = Autostart.Message ?? $"Autostart is {Autostart.Status}.";
        });
    }

    public async Task CheckForUpdatesAsync()
    {
        if (_services.UpdateChecker is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            UpdateResult = await _services.UpdateChecker.CheckAsync(_hostOptions.CurrentVersion);
            Message = UpdateResult.Message ?? (UpdateResult.IsUpdateAvailable ? "An update is available." : "You are up to date.");
        });
    }

    public async Task OpenHeatmapAsync()
    {
        if (_services.HeatmapReports is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            HeatmapGenerationResult result = await _services.HeatmapReports.GenerateAndOpenAsync();
            Message = result.Message ?? (result.Succeeded ? "Heatmap opened." : "Could not generate the heatmap.");
        });
    }

    public async Task UpdateGamesListAsync()
    {
        if (_services.GameListUpdater is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            GameListUpdateResult result = await _services.GameListUpdater.UpdateAsync();
            string message = result.Message ?? result.Status switch
            {
                GameListUpdateStatus.Updated => $"Game list updated with {result.GameCount} games.",
                GameListUpdateStatus.UpToDate => "The game list is already current.",
                _ => "The game list could not be updated."
            };
            Message = message;

            if (_services.Notifications is not null)
            {
                await _services.Notifications.ShowAsync(new UserNotification(
                    result.Status == GameListUpdateStatus.Failed ? "Game list update failed" : "Game list update complete",
                    message,
                    result.Status == GameListUpdateStatus.Failed ? NotificationSeverity.Error : NotificationSeverity.Info));
            }
        });
    }

    public Task OpenPermissionsAsync()
    {
        PermissionsWindowRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rechecks the platform consent requirements. Missing consent turns filtering
    /// and diagnostic capture off, persists that state, and prevents them from
    /// being enabled until every requirement has been granted.
    /// </summary>
    public Task RefreshInputFilteringPermissionsAsync() =>
        RunAsync(RefreshInputFilteringPermissionAsyncCore);

    public void Dispose() => _services.InputRuntime.StatusChanged -= OnRuntimeStatusChanged;

    private async Task RefreshHostStateAsync(ProfileDescriptor? activeProfile)
    {
        IReadOnlyList<ProfileDescriptor> profiles = await _services.Profiles.ListProfilesAsync();
        Profiles.Clear();
        foreach (ProfileDescriptor profile in profiles)
        {
            Profiles.Add(profile);
        }

        SetSelectedProfileWithoutActivation(
            Profiles.FirstOrDefault(profile => string.Equals(profile.Path, activeProfile?.Path, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault(profile => profile.IsDefault)
            ?? Profiles.FirstOrDefault());
        Autostart = await _services.Autostart.GetRegistrationAsync();
    }

    private async Task RefreshInputFilteringPermissionAsyncCore()
    {
        IReadOnlyList<PermissionRequirement> requirements = await _services.Permissions.GetRequiredPermissionsAsync();
        CanToggleInputFiltering = requirements.All(requirement =>
            requirement.Status is PermissionStatus.Granted or PermissionStatus.NotRequired);
        OnPropertyChanged(nameof(CanToggleDiagnostics));
        if (CanToggleInputFiltering)
        {
            return;
        }

        bool changed = _configuration.Keyboard.Enabled || _configuration.Mouse.Enabled || _configuration.Diagnostics.Enabled;
        _configuration.Keyboard.Enabled = false;
        _configuration.Mouse.Enabled = false;
        _configuration.Diagnostics.Enabled = false;
        OnPropertyChanged(nameof(KeyboardEnabled));
        OnPropertyChanged(nameof(MouseEnabled));
        OnPropertyChanged(nameof(DiagnosticsEnabled));

        if (changed && IsInitialized)
        {
            ConfigurationSaveResult save = await _services.Profiles.SaveActiveAsync(_configuration);
            if (!save.Succeeded)
            {
                Message = save.Error ?? "Could not save the disabled filtering settings.";
            }
        }

        if (Runtime.Status is InputFilterRuntimeStatus.Active or InputFilterRuntimeStatus.PermissionRequired)
        {
            Runtime = await _services.InputRuntime.StopAsync();
        }
    }

    private void QueueConfigurationUpdate()
    {
        if (!IsInitialized)
        {
            return;
        }

        _configurationRevision++;
        if (_configurationUpdateQueued)
        {
            return;
        }

        _configurationUpdateQueued = true;
        _ = ApplyQueuedConfigurationUpdatesAsync();
    }

    private async Task ApplyQueuedConfigurationUpdatesAsync()
    {
        while (true)
        {
            int revision = _configurationRevision;
            await RunAsync(async () =>
            {
                ConfigurationCompilationResult compilation = CompileConfiguration();
                ConfigurationSaveResult save = await _services.Profiles.SaveActiveAsync(_configuration);
                if (!save.Succeeded)
                {
                    Message = save.Error ?? "Could not save configuration.";
                    return;
                }

                await ApplyFilterConfigurationAsync(compilation);
                Message = Runtime.Message ?? "Configuration saved and applied.";
            });

            if (revision == _configurationRevision)
            {
                _configurationUpdateQueued = false;
                return;
            }
        }
    }

    private async Task ApplyFilterConfigurationAsync(ConfigurationCompilationResult? compilation = null)
    {
        if (!KeyboardEnabled && !MouseEnabled)
        {
            Runtime = await _services.InputRuntime.StopAsync();
            return;
        }

        Runtime = Runtime.Status == InputFilterRuntimeStatus.Active
            ? await _services.InputRuntime.ApplyConfigurationAsync(compilation ?? CompileConfiguration())
            : await _services.InputRuntime.StartAsync(compilation ?? CompileConfiguration());
    }

    private void SetSelectedProfileWithoutActivation(ProfileDescriptor? profile)
    {
        _suppressProfileActivation = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _suppressProfileActivation = false;
        }
    }

    private ConfigurationCompilationResult CompileConfiguration()
    {
        ConfigurationCompilationResult compilation = _configurationCompiler.Compile(_configuration);
        ConfigurationWarnings.Clear();
        foreach (ConfigurationWarning warning in compilation.Warnings)
        {
            ConfigurationWarnings.Add(warning);
        }

        return compilation;
    }

    private void SetConfiguration(AppConfiguration configuration)
    {
        _configuration = configuration ?? new AppConfiguration();
        _configuration.Keyboard ??= new KeyboardFilterConfiguration();
        _configuration.Mouse ??= new MouseFilterConfiguration();
        _configuration.Diagnostics ??= new DiagnosticsConfiguration();
        _configuration.Updates ??= new UpdateConfiguration();
        _configuration.Games ??= new GameProfileConfiguration();
        _configuration.Notifications ??= new NotificationConfiguration();
        OnPropertyChanged(nameof(KeyboardEnabled));
        OnPropertyChanged(nameof(MouseEnabled));
        OnPropertyChanged(nameof(KeyboardMode));
        OnPropertyChanged(nameof(KeyboardMinimumRepeatIntervalMs));
        OnPropertyChanged(nameof(MouseMinimumRepeatIntervalMs));
        OnPropertyChanged(nameof(DiagnosticsEnabled));
        OnPropertyChanged(nameof(AutoSwitchProfiles));
        OnPropertyChanged(nameof(CheckForUpdates));
        CompileConfiguration();
    }

    private void OnRuntimeStatusChanged(object? sender, InputFilterRuntimeSnapshot snapshot) =>
        PostToUi(() =>
        {
            Runtime = snapshot;
            if (snapshot.Status == InputFilterRuntimeStatus.PermissionRequired)
            {
                _ = RefreshInputFilteringPermissionsAsync();
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Message))
            {
                Message = snapshot.Message;
            }
        });

    private async Task RunAsync(Func<Task> action)
    {
        await _operationLock.WaitAsync();
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    private void PostToUi(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return;
        }

        _synchronizationContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private void RefreshCommands()
    {
        foreach (ICommand command in new[]
                 {
                     ActivateProfileCommand, CheckForUpdatesCommand, OpenPermissionsCommand, OpenHeatmapCommand,
                     UpdateGamesListCommand
                 })
        {
            if (command is AsyncCommand asyncCommand)
            {
                asyncCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
