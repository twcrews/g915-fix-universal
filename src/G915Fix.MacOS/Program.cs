using Avalonia;
using G915Fix.MacOS.Infrastructure;

namespace G915Fix.MacOS;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using MacSingleInstanceGuard? singleInstance = MacSingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // This is a resident menu-bar utility. Its settings window is
            // optional, so the running app belongs in the menu bar, not Dock.
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .UsePlatformDetect()
            .LogToTrace();
}
