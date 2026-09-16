using G915Fix.Core.Configuration;
using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;
using G915Fix.MacOS.Interop;
using G915Fix.MacOS.Services;

namespace G915Fix.MacOS.Input;

/// <summary>CoreGraphics event-tap backend. Its callback performs no I/O or asynchronous work.</summary>
internal sealed class MacInputFilterRuntime : IInputFilterRuntime, IKeyboardInputInjector, IDisposable
{
    private const long InjectionMarker = 0x47393135464958; // "G915FIX"
    private readonly object _sync = new();
    private readonly MacPermissionService _permissions;
    private readonly InputFilterRuntimeState _state = new();
    private readonly MacNative.EventTapCallback _callback;
    private readonly MacDiagnosticRouter _diagnostics;
    private readonly string _defaultDiagnosticPath;
    private readonly double _timestampFrequency;
    private IntPtr _tap;
    private IntPtr _runLoop;
    private IntPtr _source;
    private IntPtr _runLoopMode;
    private Thread? _thread;
    private KeyboardDebounceFilter? _keyboardFilter;
    private MouseDebounceFilter? _mouseFilter;
    private bool _running;
    private bool _disposed;

    public MacInputFilterRuntime(MacPermissionService permissions, MacDiagnosticRouter diagnostics, string defaultDiagnosticPath)
    {
        _permissions = permissions;
        _diagnostics = diagnostics;
        _defaultDiagnosticPath = Path.GetFullPath(defaultDiagnosticPath ?? throw new ArgumentNullException(nameof(defaultDiagnosticPath)));
        _callback = OnEvent;
        if (MacNative.mach_timebase_info(out MacNative.MachTimebaseInfo timebase) != 0 || timebase.Numer == 0)
        {
            throw new PlatformNotSupportedException("macOS monotonic timing is unavailable.");
        }

        _timestampFrequency = 1_000_000_000d * timebase.Denom / timebase.Numer;
    }

    public InputFilterRuntimeSnapshot Current => _state.Current;
    public event EventHandler<InputFilterRuntimeSnapshot>? StatusChanged
    {
        add => _state.Changed += value;
        remove => _state.Changed -= value;
    }

    public async Task<InputFilterRuntimeSnapshot> StartAsync(ConfigurationCompilationResult configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        if (!await _permissions.HasInputFilteringPermissionAsync(cancellationToken).ConfigureAwait(false))
        {
            return Publish(new InputFilterRuntimeSnapshot(
                InputFilterRuntimeStatus.PermissionRequired,
                Message: "Allow Accessibility access in System Settings before starting filtering."));
        }

        lock (_sync)
        {
            if (_running)
            {
                ReplaceFilters(configuration);
                return Publish(CreateActiveSnapshot(configuration));
            }

            ReplaceFilters(configuration);
            _state.Update(new InputFilterRuntimeSnapshot(InputFilterRuntimeStatus.Starting, Message: "Starting macOS input capture."));
        }

        try
        {
            await StartTapAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _running = true;
            }
            return Publish(CreateActiveSnapshot(configuration));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            lock (_sync)
            {
                DisposeFilters();
            }
            return Publish(new InputFilterRuntimeSnapshot(InputFilterRuntimeStatus.Faulted, Message: exception.Message));
        }
    }

    public Task<InputFilterRuntimeSnapshot> ApplyConfigurationAsync(ConfigurationCompilationResult configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        lock (_sync)
        {
            ReplaceFilters(configuration);
            return Task.FromResult(Publish(_running ? CreateActiveSnapshot(configuration) : InputFilterRuntimeSnapshot.Inactive));
        }
    }

    public Task<InputFilterRuntimeSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_running && _tap == IntPtr.Zero)
            {
                DisposeFilters();
                return Task.FromResult(Publish(InputFilterRuntimeSnapshot.Inactive));
            }

            _running = false;
            // Disposing filters while holding this lock prevents pending BlockRelease callbacks
            // from posting an event after a stop or reconfiguration.
            DisposeFilters();
            if (_runLoop != IntPtr.Zero)
            {
                MacNative.CFRunLoopStop(_runLoop);
                // CFRunLoopStop alone does not necessarily interrupt a run loop
                // sleeping for its next input event. Wake it so shutdown cannot
                // leave the UI waiting for the event-tap thread to exit.
                MacNative.CFRunLoopWakeUp(_runLoop);
            }
        }

        // Core Foundation run-loop sources must be removed and released by the
        // thread that added them. In particular, removing a source from the UI
        // thread while its input thread is still running can block indefinitely
        // in CFRunLoopRemoveSource, leaving a menu-bar Quit half completed.
        // Wait briefly for the owner to perform its cleanup, but never touch
        // those resources from this thread if it has not stopped yet.
        Thread? tapThread;
        lock (_sync)
        {
            tapThread = _thread;
        }
        if (tapThread is not null && tapThread != Thread.CurrentThread)
        {
            tapThread.Join(TimeSpan.FromSeconds(2));
        }

        return Task.FromResult(Publish(InputFilterRuntimeSnapshot.Inactive));
    }

    public void InjectKeyUp(HidKeyboardUsage key)
    {
        lock (_sync)
        {
            if (!_running || !MacKeyCodeMap.TryGetKeyCode(key, out ushort keyCode))
            {
                return;
            }

            IntPtr @event = MacNative.CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown: false);
            if (@event == IntPtr.Zero)
            {
                return;
            }

            try
            {
                MacNative.CGEventSetIntegerValueField(@event, MacNative.EventSourceUserData, InjectionMarker);
                MacNative.CGEventPost(MacNative.EventTapLocationHid, @event);
            }
            finally
            {
                MacNative.CFRelease(@event);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _diagnostics.Dispose();
        _disposed = true;
    }

    private Task StartTapAsync(CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => RunTapLoop(started))
        {
            IsBackground = true,
            Name = "G915Fix macOS input event tap"
        };
        _thread.Start();
        return WaitForTapAsync(started.Task, cancellationToken);
    }

    private static async Task WaitForTapAsync(Task<Exception?> started, CancellationToken cancellationToken)
    {
        Exception? failure = await started.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            throw new InvalidOperationException("macOS could not create the input event tap.", failure);
        }
    }

    private void RunTapLoop(TaskCompletionSource<Exception?> started)
    {
        IntPtr tap = IntPtr.Zero;
        IntPtr runLoop = IntPtr.Zero;
        IntPtr source = IntPtr.Zero;
        IntPtr mode = IntPtr.Zero;
        bool sourceAdded = false;
        try
        {
            ulong mask = EventMask(MacNative.KeyDown, MacNative.KeyUp, MacNative.FlagsChanged,
                MacNative.LeftMouseDown, MacNative.LeftMouseUp, MacNative.RightMouseDown,
                MacNative.RightMouseUp, MacNative.OtherMouseDown, MacNative.OtherMouseUp);
            tap = MacNative.CGEventTapCreate(
                MacNative.EventTapLocationHid,
                MacNative.EventTapPlacementHeadInsert,
                MacNative.EventTapOptionsDefault,
                mask,
                _callback,
                IntPtr.Zero);
            if (tap == IntPtr.Zero)
            {
                throw new UnauthorizedAccessException("CoreGraphics rejected the event tap. Check Accessibility permission.");
            }

            runLoop = MacNative.CFRunLoopGetCurrent();
            mode = MacNative.CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", MacNative.Utf8StringEncoding);
            source = MacNative.CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
            if (source == IntPtr.Zero || mode == IntPtr.Zero)
            {
                throw new InvalidOperationException("CoreFoundation could not attach the input event tap to a run loop.");
            }

            lock (_sync)
            {
                _tap = tap;
                _runLoop = runLoop;
                _source = source;
                _runLoopMode = mode;
            }
            MacNative.CFRunLoopAddSource(runLoop, source, mode);
            sourceAdded = true;
            MacNative.CGEventTapEnable(tap, true);
            started.TrySetResult(null);
            MacNative.CFRunLoopRun();
        }
        catch (Exception exception)
        {
            started.TrySetResult(exception);
            Publish(new InputFilterRuntimeSnapshot(InputFilterRuntimeStatus.Faulted, Message: exception.Message));
        }
        finally
        {
            // CFRunLoop and its sources have thread affinity. This is the only
            // place they are detached and released, including failed startup.
            if (sourceAdded)
            {
                MacNative.CFRunLoopRemoveSource(runLoop, source, mode);
            }
            if (source != IntPtr.Zero) MacNative.CFRelease(source);
            if (mode != IntPtr.Zero) MacNative.CFRelease(mode);
            if (tap != IntPtr.Zero) MacNative.CFRelease(tap);

            lock (_sync)
            {
                if (_tap == tap)
                {
                    _tap = _runLoop = _source = _runLoopMode = IntPtr.Zero;
                }
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                }
            }
        }
    }

    private IntPtr OnEvent(IntPtr proxy, nuint eventType, IntPtr @event, IntPtr userInfo)
    {
        if (eventType is MacNative.EventTapDisabledByTimeout or MacNative.EventTapDisabledByUserInput)
        {
            lock (_sync)
            {
                if (_running && _tap != IntPtr.Zero)
                {
                    MacNative.CGEventTapEnable(_tap, true);
                }
            }
            return @event;
        }

        try
        {
            lock (_sync)
            {
                if (!_running || @event == IntPtr.Zero)
                {
                    return @event;
                }

                bool injected = MacNative.CGEventGetIntegerValueField(@event, MacNative.EventSourceUserData) == InjectionMarker
                    || MacNative.CGEventGetIntegerValueField(@event, MacNative.EventSourceUnixProcessId) != 0;
                ulong timestamp = MacNative.CGEventGetTimestamp(@event);
                if (TryCreateKeyboardEvent(eventType, @event, injected, timestamp, out KeyboardInputEvent keyboard))
                {
                    return _keyboardFilter?.ShouldSuppress(keyboard) == true ? IntPtr.Zero : @event;
                }

                if (TryCreateMouseEvent(eventType, @event, timestamp, out MouseInputEvent mouse))
                {
                    return _mouseFilter?.ShouldSuppress(mouse) == true ? IntPtr.Zero : @event;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // A tap callback racing shutdown must always leave the original event alone.
        }
        catch
        {
            // Input capture must fail open; a bad native event can never block typing.
        }

        return @event;
    }

    private static bool TryCreateKeyboardEvent(nuint eventType, IntPtr @event, bool injected, ulong timestamp, out KeyboardInputEvent input)
    {
        input = default;
        if (eventType is not (MacNative.KeyDown or MacNative.KeyUp or MacNative.FlagsChanged))
        {
            return false;
        }

        long rawKeyCode = MacNative.CGEventGetIntegerValueField(@event, MacNative.KeyboardEventKeyCode);
        if (rawKeyCode is < 0 or > ushort.MaxValue || !MacKeyCodeMap.TryGetUsage((ushort)rawKeyCode, out HidKeyboardUsage usage))
        {
            return false;
        }

        KeyboardInputKind kind;
        if (eventType == MacNative.FlagsChanged)
        {
            if (!MacKeyCodeMap.TryGetModifierKind((ushort)rawKeyCode, MacNative.CGEventGetFlags(@event), out kind))
            {
                return false;
            }
        }
        else
        {
            kind = eventType == MacNative.KeyDown ? KeyboardInputKind.KeyDown : KeyboardInputKind.KeyUp;
        }

        input = new KeyboardInputEvent(usage, kind, injected, checked((long)timestamp));
        return true;
    }

    private static bool TryCreateMouseEvent(nuint eventType, IntPtr @event, ulong timestamp, out MouseInputEvent input)
    {
        input = default;
        bool down = eventType is MacNative.LeftMouseDown or MacNative.RightMouseDown or MacNative.OtherMouseDown;
        if (!down && eventType is not (MacNative.LeftMouseUp or MacNative.RightMouseUp or MacNative.OtherMouseUp))
        {
            return false;
        }

        int button = eventType is MacNative.LeftMouseDown or MacNative.LeftMouseUp ? 0
            : eventType is MacNative.RightMouseDown or MacNative.RightMouseUp ? 1
            : checked((int)MacNative.CGEventGetIntegerValueField(@event, MacNative.MouseEventButtonNumber));
        if (button is < 0 or > 4)
        {
            return false;
        }

        input = new MouseInputEvent(new MouseButton(button), down ? MouseInputKind.ButtonDown : MouseInputKind.ButtonUp, checked((long)timestamp));
        return true;
    }

    private void ReplaceFilters(ConfigurationCompilationResult configuration)
    {
        DiagnosticRuntimeOptions? diagnosticOptions = configuration.Diagnostics;
        if (diagnosticOptions is { Enabled: true } && string.IsNullOrWhiteSpace(diagnosticOptions.LogPath))
        {
            diagnosticOptions = new DiagnosticRuntimeOptions(true, _defaultDiagnosticPath);
        }
        _diagnostics.Configure(diagnosticOptions);
        KeyboardDebounceFilter? replacementKeyboard = configuration.KeyboardEnabled
            ? new KeyboardDebounceFilter(configuration.KeyboardOptions, this, timestampFrequency: _timestampFrequency, diagnosticSink: _diagnostics)
            : null;
        MouseDebounceFilter? replacementMouse = configuration.MouseEnabled
            ? new MouseDebounceFilter(configuration.MouseOptions, timestampFrequency: _timestampFrequency, diagnosticSink: _diagnostics)
            : null;
        KeyboardDebounceFilter? oldKeyboard = _keyboardFilter;
        _keyboardFilter = replacementKeyboard;
        _mouseFilter = replacementMouse;
        oldKeyboard?.Dispose();
    }

    private void DisposeFilters()
    {
        _keyboardFilter?.Dispose();
        _keyboardFilter = null;
        _mouseFilter = null;
    }

    private InputFilterRuntimeSnapshot CreateActiveSnapshot(ConfigurationCompilationResult configuration) =>
        new(InputFilterRuntimeStatus.Active, configuration.KeyboardEnabled, configuration.MouseEnabled, "Input filtering is active.");

    private InputFilterRuntimeSnapshot Publish(InputFilterRuntimeSnapshot snapshot)
    {
        _state.Update(snapshot);
        return snapshot;
    }

    private static ulong EventMask(params nuint[] eventTypes) => eventTypes.Aggregate(0UL, static (mask, type) => mask | (1UL << (int)type));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
