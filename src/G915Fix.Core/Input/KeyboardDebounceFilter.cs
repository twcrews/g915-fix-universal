using System.Diagnostics;
using G915Fix.Core.Diagnostics;

namespace G915Fix.Core.Input;

/// <summary>
/// Platform-neutral keyboard debounce state machine. A native input adapter must
/// invoke <see cref="ShouldSuppress"/> synchronously before delivering an event.
/// </summary>
public sealed class KeyboardDebounceFilter : IKeyboardInputFilter, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<HidKeyboardUsage, KeyState> _states = [];
    private readonly HashSet<HidKeyboardUsage> _excludedKeys;
    private readonly Dictionary<HidKeyboardUsage, long> _thresholds;
    private readonly IKeyboardInputInjector _injector;
    private readonly IReleaseSchedulerFactory _schedulerFactory;
    private readonly Func<long> _getTimestamp;
    private readonly long _defaultThreshold;
    private readonly double _timestampFrequency;
    private readonly long _burstGap;
    private readonly KeyboardDebounceMode _mode;
    private readonly bool _burstBypass;
    private readonly int _burstMinimumStreak;
    private readonly IFilterDiagnosticSink? _diagnosticSink;

    private long _lastDownTimestamp;
    private int _rapidStreak;
    private bool _inBurst;
    private bool _disposed;

    public KeyboardDebounceFilter(
        KeyboardDebounceOptions options,
        IKeyboardInputInjector injector,
        IReleaseSchedulerFactory? schedulerFactory = null,
        Func<long>? getTimestamp = null,
        double? timestampFrequency = null,
        IFilterDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(injector);
        ValidateOptions(options);

        double frequency = timestampFrequency ?? Stopwatch.Frequency;
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _injector = injector;
        _schedulerFactory = schedulerFactory ?? new TimerReleaseSchedulerFactory();
        _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        _timestampFrequency = frequency;
        _mode = options.Mode;
        _burstBypass = options.EnableBurstBypass;
        _burstMinimumStreak = options.BurstMinimumStreak;
        _diagnosticSink = diagnosticSink;
        _defaultThreshold = ToTimestampTicks(options.MinimumRepeatInterval, frequency);
        _burstGap = ToTimestampTicks(options.BurstGap, frequency);
        _excludedKeys = new HashSet<HidKeyboardUsage>(options.ExcludedKeys)
        {
            HidKeyboardUsage.CapsLock
        };
        _thresholds = options.PerKeyMinimumRepeatIntervals.ToDictionary(
            pair => pair.Key,
            pair => ToTimestampTicks(pair.Value, frequency));
    }

    public bool ShouldSuppress(KeyboardInputEvent inputEvent)
    {
        FilterDiagnosticAction? action = null;
        bool suppress;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (inputEvent.IsInjected || !IsKeyEvent(inputEvent.Kind))
            {
                return false;
            }

            long now = inputEvent.Timestamp ?? _getTimestamp();
            if (_burstBypass)
            {
                UpdateBurstState(inputEvent.Kind, now);
            }

            if (_excludedKeys.Contains(inputEvent.Key))
            {
                return false;
            }

            KeyState state = GetState(inputEvent.Key);
            suppress = _mode == KeyboardDebounceMode.BlockRelease
                ? HandleBlockRelease(inputEvent.Key, inputEvent.Kind, state, out action)
                : HandleBlockRepress(inputEvent.Kind, now, state, out action);
        }

        RecordDiagnostic(inputEvent.Key, action);
        return suppress;
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
            foreach (KeyState state in _states.Values)
            {
                state.Scheduler?.Dispose();
            }

            _states.Clear();
        }
    }

    private bool HandleBlockRepress(
        KeyboardInputKind kind,
        long now,
        KeyState state,
        out FilterDiagnosticAction? action)
    {
        action = null;
        if (IsKeyDown(kind))
        {
            if (!state.IsPressed
                && state.HasLastUpTimestamp
                && now - state.LastUpTimestamp < GetThreshold(state.Key)
                && !_inBurst)
            {
                state.SwallowNextUp = true;
                action = FilterDiagnosticAction.RepressBlocked;
                return true;
            }

            state.IsPressed = true;
            state.SwallowNextUp = false;
            return false;
        }

        if (state.SwallowNextUp)
        {
            state.SwallowNextUp = false;
            return true;
        }

        state.LastUpTimestamp = now;
        state.HasLastUpTimestamp = true;
        state.IsPressed = false;
        return false;
    }

    private bool HandleBlockRelease(
        HidKeyboardUsage key,
        KeyboardInputKind kind,
        KeyState state,
        out FilterDiagnosticAction? action)
    {
        action = null;
        if (_inBurst)
        {
            if (state.PendingUp)
            {
                CancelPendingUp(state);
            }

            state.IsPressed = IsKeyDown(kind);
            return false;
        }

        if (IsKeyDown(kind))
        {
            if (state.PendingUp)
            {
                CancelPendingUp(state);
                action = FilterDiagnosticAction.ReleaseHeld;
                return true;
            }

            state.IsPressed = true;
            return false;
        }

        if (state.PendingUp)
        {
            return true;
        }

        if (!state.IsPressed)
        {
            return false;
        }

        state.PendingUp = true;
        state.Scheduler ??= _schedulerFactory.Create();
        long releaseVersion = ++state.ReleaseVersion;
        state.Scheduler.Schedule(key, GetDelay(state.Key), dueKey => OnReleaseDue(dueKey, releaseVersion));
        return true;
    }

    private void RecordDiagnostic(HidKeyboardUsage key, FilterDiagnosticAction? action)
    {
        if (action is null || _diagnosticSink is null)
        {
            return;
        }

        try
        {
            _diagnosticSink.Record(new FilterDiagnosticEvent(
                FilterDiagnosticEvent.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                FilterDiagnosticEventKind.KeyboardFiltered,
                Key: key,
                Action: action));
        }
        catch
        {
            // Diagnostics must never alter filtering behavior.
        }
    }

    private void CancelPendingUp(KeyState state)
    {
        state.PendingUp = false;
        state.ReleaseVersion++;
        state.Scheduler?.Cancel();
    }

    private void OnReleaseDue(HidKeyboardUsage key, long releaseVersion)
    {
        bool inject = false;
        lock (_sync)
        {
            if (_disposed
                || !_states.TryGetValue(key, out KeyState? state)
                || !state.PendingUp
                || state.ReleaseVersion != releaseVersion)
            {
                return;
            }

            state.PendingUp = false;
            state.IsPressed = false;
            inject = true;
        }

        if (inject)
        {
            _injector.InjectKeyUp(key);
        }
    }

    private void UpdateBurstState(KeyboardInputKind kind, long now)
    {
        if (!IsKeyDown(kind))
        {
            return;
        }

        if (_lastDownTimestamp != 0 && now - _lastDownTimestamp < _burstGap)
        {
            _rapidStreak = Math.Min(_rapidStreak + 1, _burstMinimumStreak);
        }
        else
        {
            _rapidStreak = 0;
        }

        _lastDownTimestamp = now;
        _inBurst = _rapidStreak >= _burstMinimumStreak;
    }

    private KeyState GetState(HidKeyboardUsage key)
    {
        if (!_states.TryGetValue(key, out KeyState? state))
        {
            state = new KeyState(key);
            _states.Add(key, state);
        }

        return state;
    }

    private long GetThreshold(HidKeyboardUsage key) =>
        _thresholds.TryGetValue(key, out long threshold) ? threshold : _defaultThreshold;

    private TimeSpan GetDelay(HidKeyboardUsage key) =>
        _thresholds.TryGetValue(key, out long threshold)
            ? TimeSpan.FromSeconds((double)threshold / _timestampFrequency)
            : TimeSpan.FromSeconds((double)_defaultThreshold / _timestampFrequency);

    private static bool IsKeyEvent(KeyboardInputKind kind) => IsKeyDown(kind) || IsKeyUp(kind);

    private static bool IsKeyDown(KeyboardInputKind kind) =>
        kind is KeyboardInputKind.KeyDown or KeyboardInputKind.SystemKeyDown;

    private static bool IsKeyUp(KeyboardInputKind kind) =>
        kind is KeyboardInputKind.KeyUp or KeyboardInputKind.SystemKeyUp;

    private static long ToTimestampTicks(TimeSpan duration, double frequency) =>
        checked((long)Math.Ceiling(duration.TotalSeconds * frequency));

    private static void ValidateOptions(KeyboardDebounceOptions options)
    {
        if (options.MinimumRepeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MinimumRepeatInterval));
        }

        if (options.BurstGap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BurstGap));
        }

        if (options.BurstMinimumStreak < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BurstMinimumStreak));
        }

        if (options.PerKeyMinimumRepeatIntervals.Any(pair => pair.Value < TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(options.PerKeyMinimumRepeatIntervals));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class KeyState(HidKeyboardUsage key)
    {
        public HidKeyboardUsage Key { get; } = key;
        public long LastUpTimestamp { get; set; }
        public bool HasLastUpTimestamp { get; set; }
        public bool IsPressed { get; set; }
        public bool SwallowNextUp { get; set; }
        public bool PendingUp { get; set; }
        public long ReleaseVersion { get; set; }
        public IReleaseScheduler? Scheduler { get; set; }
    }
}
