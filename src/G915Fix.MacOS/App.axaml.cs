using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using G915Fix.Desktop.ViewModels;
using G915Fix.MacOS.Infrastructure;

namespace G915Fix.MacOS;

public partial class App : Application
{
    private MacHost? _host;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _filterKeyboard;
    private NativeMenuItem? _filterMouse;
    private NativeMenuItem? _profileAutoSwitch;
    private NativeMenuItem? _trackEvents;
    private int _shutdownRequested;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = MacHostFactory.Create();
            // This resident app starts from the menu bar. Do not assign a main
            // window to the desktop lifetime, which would show it at launch.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = new MainWindow { DataContext = _host.ViewModel };
            CreateMenuBarIcon(desktop);
            desktop.Exit += (_, _) =>
            {
                _tray?.Dispose();
                _tray = null;
                if (_host is not null)
                {
                    _host.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    _host.Dispose();
                }
            };
            ActualThemeVariantChanged += (_, _) => UpdateMenuBarIcon();
            Dispatcher.UIThread.Post(async () =>
            {
                await _host.ViewModel.LoadAsync();
                if (_host.ViewModel.HasMissingPermissions)
                {
                    ShowWindow();
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateMenuBarIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        DesktopMainViewModel viewModel = _host?.ViewModel
            ?? throw new InvalidOperationException("The application host is unavailable.");
        _filterKeyboard = CreateToggleMenuItem("Filter keyboard", viewModel.KeyboardEnabled,
            isEnabled => viewModel.KeyboardEnabled = isEnabled);
        _filterMouse = CreateToggleMenuItem("Filter mouse", viewModel.MouseEnabled,
            isEnabled => viewModel.MouseEnabled = isEnabled);
        _filterKeyboard.IsEnabled = viewModel.CanToggleInputFiltering;
        _filterMouse.IsEnabled = viewModel.CanToggleInputFiltering;
        _profileAutoSwitch = CreateToggleMenuItem("Profile auto-switch", viewModel.AutoSwitchProfiles,
            isEnabled => viewModel.AutoSwitchProfiles = isEnabled);
        _trackEvents = CreateToggleMenuItem("Track events", viewModel.DiagnosticsEnabled,
            isEnabled => viewModel.DiagnosticsEnabled = isEnabled);
        _trackEvents.IsEnabled = viewModel.CanToggleDiagnostics;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        var updateGames = new NativeMenuItem("Update games list...")
        {
            Command = viewModel.UpdateGamesListCommand
        };
        var heatmap = new NativeMenuItem("Event heatmap...")
        {
            Command = viewModel.OpenHeatmapCommand
        };
        var settings = new NativeMenuItem("All settings...");
        settings.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Quit G915 Fix");
        quit.Click += (_, _) => RequestShutdown(desktop);

        var menu = new NativeMenu();
        menu.Items.Add(_filterKeyboard);
        menu.Items.Add(_filterMouse);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_profileAutoSwitch);
        menu.Items.Add(updateGames);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_trackEvents);
        menu.Items.Add(heatmap);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            ToolTipText = "G915 Fix",
            Menu = menu
        };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        UpdateMenuBarIcon();
    }

    private void RequestShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        // An NSMenu action is still active while this handler runs. Defer app
        // teardown until it returns; shutting down from within that action can
        // leave the macOS menu-bar event loop waiting on itself.
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is not null) _window.AllowClose = true;
            desktop.Shutdown();
        }, DispatcherPriority.Background);
    }

    private static NativeMenuItem CreateToggleMenuItem(string header, bool isChecked, Action<bool> setValue)
    {
        var item = new NativeMenuItem(header)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = isChecked
        };
        item.Click += (_, _) =>
        {
            // Native menu providers report activation but do not change this
            // Avalonia property. Toggle it before updating the view model.
            bool enabled = item.IsChecked != true;
            item.IsChecked = enabled;
            setValue(enabled);
        };
        return item;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DesktopMainViewModel.CanToggleInputFiltering):
                if (_filterKeyboard is not null) _filterKeyboard.IsEnabled = _host.ViewModel.CanToggleInputFiltering;
                if (_filterMouse is not null) _filterMouse.IsEnabled = _host.ViewModel.CanToggleInputFiltering;
                break;
            case nameof(DesktopMainViewModel.CanToggleDiagnostics):
                if (_trackEvents is not null) _trackEvents.IsEnabled = _host.ViewModel.CanToggleDiagnostics;
                break;
            case nameof(DesktopMainViewModel.KeyboardEnabled):
                _filterKeyboard?.IsChecked = _host.ViewModel.KeyboardEnabled;
                break;
            case nameof(DesktopMainViewModel.MouseEnabled):
                _filterMouse?.IsChecked = _host.ViewModel.MouseEnabled;
                break;
            case nameof(DesktopMainViewModel.AutoSwitchProfiles):
                _profileAutoSwitch?.IsChecked = _host.ViewModel.AutoSwitchProfiles;
                break;
            case nameof(DesktopMainViewModel.DiagnosticsEnabled):
                _trackEvents?.IsChecked = _host.ViewModel.DiagnosticsEnabled;
                break;
        }
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void UpdateMenuBarIcon()
    {
        if (_tray is null)
        {
            return;
        }

        // The supplied black/white assets are intentionally used for menu-bar
        // contrast; app-icon.icon remains the authored application icon asset.
        string icon = ActualThemeVariant == ThemeVariant.Dark
            ? "avares://G915Fix.MacOS/Assets/app-icon-white.png"
            : "avares://G915Fix.MacOS/Assets/app-icon-black.png";
        _tray.Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri(icon))));
    }
}
