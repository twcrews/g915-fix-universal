using System.Collections.ObjectModel;
using System.Windows.Input;
using G915Fix.Core.Autostart;
using G915Fix.Core.Configuration;
using G915Fix.Core.Input;
using G915Fix.Core.Heatmap;
using G915Fix.Core.Permissions;
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
    private PermissionRequirement? _selectedPermission;
    private InputFilterRuntimeSnapshot _runtime = InputFilterRuntimeSnapshot.Inactive;
    private AutostartRegistration? _autostart;
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
        ToggleAutostartCommand = new AsyncCommand(ToggleAutostartAsync, () => !IsBusy && Autostart?.Status is AutostartStatus.Enabled or AutostartStatus.Disabled);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync, () => !IsBusy && _services.UpdateChecker is not null);
        RequestPermissionCommand = new AsyncCommand(RequestSelectedPermissionAsync, () => !IsBusy && SelectedPermission is not null);
        OpenHeatmapCommand = new AsyncCommand(OpenHeatmapAsync, () => !IsBusy && _services.HeatmapReports is not null);

        _services.InputRuntime.StatusChanged += OnRuntimeStatusChanged;
    }

    public string ApplicationName => _hostOptions.ApplicationName;
    public string VersionText => _hostOptions.CurrentVersion.ToString();
    public IReadOnlyList<string> KeyboardModes { get; } = Enum.GetNames<KeyboardDebounceMode>();
    public ObservableCollection<ProfileDescriptor> Profiles { get; } = [];
    public ObservableCollection<PermissionRequirement> Permissions { get; } = [];
    public ObservableCollection<ConfigurationWarning> ConfigurationWarnings { get; } = [];

    public ICommand InitializeCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ActivateProfileCommand { get; }
    public ICommand ToggleAutostartCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand RequestPermissionCommand { get; }
    public ICommand OpenHeatmapCommand { get; }

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
                RefreshCommands();
            }
        }
    }

    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public InputFilterRuntimeSnapshot Runtime { get => _runtime; private set => SetProperty(ref _runtime, value); }
    public AutostartRegistration? Autostart { get => _autostart; private set => SetProperty(ref _autostart, value); }
    public UpdateCheckResult? UpdateResult { get => _updateResult; private set => SetProperty(ref _updateResult, value); }

    public ProfileDescriptor? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RefreshCommands();
            }
        }
    }

    public PermissionRequirement? SelectedPermission
    {
        get => _selectedPermission;
        set
        {
            if (SetProperty(ref _selectedPermission, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool KeyboardEnabled
    {
        get => _configuration.Keyboard.Enabled;
        set { _configuration.Keyboard.Enabled = value; OnPropertyChanged(); }
    }

    public bool MouseEnabled
    {
        get => _configuration.Mouse.Enabled;
        set { _configuration.Mouse.Enabled = value; OnPropertyChanged(); }
    }

    public string KeyboardMode
    {
        get => _configuration.Keyboard.Mode;
        set { _configuration.Keyboard.Mode = value; OnPropertyChanged(); }
    }

    public double KeyboardMinimumRepeatIntervalMs
    {
        get => _configuration.Keyboard.MinimumRepeatIntervalMs;
        set { _configuration.Keyboard.MinimumRepeatIntervalMs = value; OnPropertyChanged(); }
    }

    public double MouseMinimumRepeatIntervalMs
    {
        get => _configuration.Mouse.MinimumRepeatIntervalMs;
        set { _configuration.Mouse.MinimumRepeatIntervalMs = value; OnPropertyChanged(); }
    }

    public bool DiagnosticsEnabled
    {
        get => _configuration.Diagnostics.Enabled;
        set { _configuration.Diagnostics.Enabled = value; OnPropertyChanged(); }
    }

    public bool AutoSwitchProfiles
    {
        get => _configuration.Games.AutoSwitchProfiles;
        set { _configuration.Games.AutoSwitchProfiles = value; OnPropertyChanged(); }
    }

    public bool CheckForUpdates
    {
        get => _configuration.Updates.CheckForUpdates;
        set { _configuration.Updates.CheckForUpdates = value; OnPropertyChanged(); }
    }

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            ProfileActivationResult activation = await _services.Profiles.InitializeAsync();
            SetConfiguration(activation.ActiveConfiguration ?? new AppConfiguration());
            await RefreshHostStateAsync();
            IsInitialized = activation.Succeeded;
            Message = activation.Message ?? (activation.Succeeded ? "Configuration loaded." : "Configuration could not be loaded.");
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

    public async Task ToggleAutostartAsync()
    {
        await RunAsync(async () =>
        {
            Autostart = Autostart?.IsEnabled == true
                ? await _services.Autostart.DisableAsync()
                : await _services.Autostart.EnableAsync();
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

    public async Task RequestSelectedPermissionAsync()
    {
        if (SelectedPermission is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            PermissionRequestResult result = await _services.Permissions.RequestPermissionAsync(SelectedPermission.Id);
            Message = result.Message ?? "Follow the macOS permission prompt, then initialize or start filtering again.";
            IReadOnlyList<PermissionRequirement> permissions = await _services.Permissions.GetRequiredPermissionsAsync();
            Permissions.Clear();
            foreach (PermissionRequirement permission in permissions)
            {
                Permissions.Add(permission);
            }
            SelectedPermission = Permissions.FirstOrDefault(permission => permission.Id == SelectedPermission?.Id);
        });
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

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.IsDefault) ?? Profiles.FirstOrDefault();
        Autostart = await _services.Autostart.GetRegistrationAsync();

        IReadOnlyList<PermissionRequirement> permissions = await _services.Permissions.GetRequiredPermissionsAsync();
        Permissions.Clear();
        foreach (PermissionRequirement permission in permissions)
        {
            Permissions.Add(permission);
        }
        SelectedPermission = Permissions.FirstOrDefault(permission => permission.Status != PermissionStatus.Granted)
            ?? Permissions.FirstOrDefault();
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
        if (IsBusy)
        {
            return;
        }

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
                     ActivateProfileCommand, ToggleAutostartCommand, CheckForUpdatesCommand, RequestPermissionCommand,
                     OpenHeatmapCommand
                 })
        {
            if (command is AsyncCommand asyncCommand)
            {
                asyncCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
