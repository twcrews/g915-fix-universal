namespace G915Fix.Core.Input;

/// <summary>Creates one-shot release schedulers backed by <see cref="Timer"/>.</summary>
public sealed class TimerReleaseSchedulerFactory : IReleaseSchedulerFactory
{
    public IReleaseScheduler Create() => new TimerReleaseScheduler();
}

/// <summary>A thread-safe one-shot release scheduler.</summary>
public sealed class TimerReleaseScheduler : IReleaseScheduler
{
    private readonly object _sync = new();
    private Timer? _timer;
    private long _generation;
    private bool _disposed;

    public void Schedule(HidKeyboardUsage key, TimeSpan delay, Action<HidKeyboardUsage> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer?.Dispose();
            long generation = ++_generation;
            _timer = new Timer(_ => InvokeIfCurrent(generation, key, callback), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _generation++;
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void InvokeIfCurrent(long generation, HidKeyboardUsage key, Action<HidKeyboardUsage> callback)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }
        }

        callback(key);
    }
}
