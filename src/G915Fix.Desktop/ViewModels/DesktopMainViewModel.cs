using System.Collections.ObjectModel;
using System.Windows.Input;
using G915Fix.Core.Autostart;
using G915Fix.Core.Configuration;
using G915Fix.Core.Input;
using G915Fix.Core.Games;
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

        InitializeCommand = new AsyncCommand(InitializeAsync, () => !IsBusy);
        StartCommand = new AsyncCommand(StartAsync, () => !IsBusy);
        StopCommand = new AsyncCommand(StopAsync, () => !IsBusy && Runtime.Status != InputFilterRuntimeStatus.Inactive);
        SaveCommand = new AsyncCommand(SaveAsync, () => !IsBusy && IsInitialized);
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

    public ICommand InitializeCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveCommand { get; }
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

    public bool KeyboardEnabled
    {
        get => _configuration.Keyboard.Enabled;
        set { _configuration.Keyboard.Enabled = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
    }

    public bool MouseEnabled
    {
        get => _configuration.Mouse.Enabled;
        set { _configuration.Mouse.Enabled = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
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
        set { _configuration.Diagnostics.Enabled = value; OnPropertyChanged(); QueueConfigurationUpdate(); }
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

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            ProfileActivationResult activation = await _services.Profiles.InitializeAsync();
            SetConfiguration(activation.ActiveConfiguration ?? new AppConfiguration());
            await RefreshHostStateAsync();
            IsInitialized = activation.Succeeded;
            if (activation.Succeeded)
            {
                Runtime = await _services.InputRuntime.StartAsync(CompileConfiguration());
            }

            Message = activation.Message ?? (activation.Succeeded ? Runtime.Message ?? "Configuration loaded and filtering started." : "Configuration could not be loaded.");
        });
    }

    public async Task StartAsync()
    {
        await RunAsync(async () =>
        {
            if (!IsInitialized)
            {
                ProfileActivationResult activation = await _services.Profiles.InitializeAsync();
                SetConfiguration(activation.ActiveConfiguration ?? new AppConfiguration());
                IsInitialized = activation.Succeeded;
                if (!activation.Succeeded)
                {
                    Message = activation.Message ?? "Configuration could not be loaded.";
                    return;
                }
            }

            Runtime = await _services.InputRuntime.StartAsync(CompileConfiguration());
            Message = Runtime.Message ?? "Input filtering started.";
        });
    }

    public async Task StopAsync()
    {
        await RunAsync(async () =>
        {
            Runtime = await _services.InputRuntime.StopAsync();
            Message = Runtime.Message ?? "Input filtering stopped.";
        });
    }

    public async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            ConfigurationCompilationResult compilation = CompileConfiguration();
            var save = await _services.Profiles.SaveActiveAsync(_configuration);
            if (!save.Succeeded)
            {
                Message = save.Error ?? "Could not save configuration.";
                return;
            }

            Runtime = await _services.InputRuntime.ApplyConfigurationAsync(compilation);
            Message = Runtime.Message ?? "Configuration saved and applied.";
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
            SelectedProfile = activation.ActiveProfile;
            Runtime = await _services.InputRuntime.ApplyConfigurationAsync(CompileConfiguration());
            Message = activation.Message ?? $"Activated {SelectedProfile?.Name}.";
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
            Message = result.Message ?? (result.Status == GameListUpdateStatus.Updated
                ? $"Game list updated with {result.GameCount} games."
                : "The game list is already current.");
        });
    }

    public Task OpenPermissionsAsync()
    {
        PermissionsWindowRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dispose() => _services.InputRuntime.StatusChanged -= OnRuntimeStatusChanged;

    private async Task RefreshHostStateAsync()
    {
        IReadOnlyList<ProfileDescriptor> profiles = await _services.Profiles.ListProfilesAsync();
        Profiles.Clear();
        foreach (ProfileDescriptor profile in profiles)
        {
            Profiles.Add(profile);
        }

        SetSelectedProfileWithoutActivation(Profiles.FirstOrDefault(profile => profile.IsDefault) ?? Profiles.FirstOrDefault());
        Autostart = await _services.Autostart.GetRegistrationAsync();
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

                Runtime = Runtime.Status == InputFilterRuntimeStatus.Active
                    ? await _services.InputRuntime.ApplyConfigurationAsync(compilation)
                    : await _services.InputRuntime.StartAsync(compilation);
                Message = Runtime.Message ?? "Configuration saved and applied.";
            });

            if (revision == _configurationRevision)
            {
                _configurationUpdateQueued = false;
                return;
            }
        }
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
                     InitializeCommand, StartCommand, StopCommand, SaveCommand,
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
