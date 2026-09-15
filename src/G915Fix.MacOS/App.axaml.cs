using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using G915Fix.MacOS.Infrastructure;

namespace G915Fix.MacOS;

public partial class App : Application
{
    private MacHost? _host;
    private MainWindow? _window;
    private PermissionsWindow? _permissionsWindow;
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = MacHostFactory.Create();
            _host.ViewModel.PermissionsWindowRequested += OnPermissionsWindowRequested;
            _window = new MainWindow { DataContext = _host.ViewModel };
            desktop.MainWindow = _window;
            CreateMenuBarIcon(desktop);
            desktop.Exit += (_, _) => _host.Dispose();
            ActualThemeVariantChanged += (_, _) => UpdateMenuBarIcon();
            Dispatcher.UIThread.Post(async () =>
            {
                await _host.ViewModel.InitializeAsync();
                if (await _host.PermissionsViewModel.RefreshAsync())
                {
                    ShowPermissionsWindow();
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateMenuBarIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("Open G915 Fix");
        open.Click += (_, _) => ShowWindow();
        var permissions = new NativeMenuItem("Permissions...");
        permissions.Click += (_, _) => ShowPermissionsWindow();
        var quit = new NativeMenuItem("Quit G915 Fix");
        quit.Click += (_, _) =>
        {
            if (_window is not null) _window.AllowClose = true;
            if (_permissionsWindow is not null) _permissionsWindow.AllowClose = true;
            desktop.Shutdown();
        };
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(permissions);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            ToolTipText = "G915 Fix",
            Menu = menu
        };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        UpdateMenuBarIcon();
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

    private void OnPermissionsWindowRequested(object? sender, EventArgs e) => ShowPermissionsWindow();

    private void ShowPermissionsWindow()
    {
        if (_host is null)
        {
            return;
        }

        _permissionsWindow ??= new PermissionsWindow(_host.PermissionsViewModel);
        _permissionsWindow.Show();
        _permissionsWindow.WindowState = WindowState.Normal;
        _permissionsWindow.Activate();
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
