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
            .UsePlatformDetect()
            .LogToTrace();
}
