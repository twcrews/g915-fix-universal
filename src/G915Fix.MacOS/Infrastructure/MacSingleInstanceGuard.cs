namespace G915Fix.MacOS.Infrastructure;

/// <summary>
/// Holds a per-user, process-lifetime mutex so a manually launched copy and a
/// launchd-started copy cannot both run the input filter.
/// </summary>
internal sealed class MacSingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;
    private readonly bool _ownsMutex;

    private MacSingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    /// <summary>
    /// Acquires the G915 Fix mutex, or returns <see langword="null"/> when an
    /// instance is already running for this macOS user.
    /// </summary>
    public static MacSingleInstanceGuard? TryAcquire()
    {
        string name = $"com.twcrews.g915fix.macos.instance.{getuid()}";
        var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new MacSingleInstanceGuard(mutex, ownsMutex: true);
    }

    public void Dispose()
    {
        Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            if (_ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
        finally
        {
            mutex.Dispose();
        }
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint getuid();
}
