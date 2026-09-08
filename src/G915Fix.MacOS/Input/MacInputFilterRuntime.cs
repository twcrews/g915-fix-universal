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

    public MacInputFilterRuntime(MacPermissionService permissions, MacDiagnosticRouter diagnostics)
    {
        _permissions = permissions;
        _diagnostics = diagnostics;
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
                Message: "Allow Accessibility and Input Monitoring access in System Settings before starting filtering."));
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
            }
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        ReleaseTapResources();
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
        try
        {
            ulong mask = EventMask(MacNative.KeyDown, MacNative.KeyUp, MacNative.FlagsChanged,
                MacNative.LeftMouseDown, MacNative.LeftMouseUp, MacNative.RightMouseDown,
                MacNative.RightMouseUp, MacNative.OtherMouseDown, MacNative.OtherMouseUp);
            IntPtr tap = MacNative.CGEventTapCreate(
                MacNative.EventTapLocationHid,
                MacNative.EventTapPlacementHeadInsert,
                MacNative.EventTapOptionsDefault,
                mask,
                _callback,
                IntPtr.Zero);
            if (tap == IntPtr.Zero)
            {
                throw new UnauthorizedAccessException("CoreGraphics rejected the event tap. Check Accessibility and Input Monitoring permissions.");
            }

            IntPtr runLoop = MacNative.CFRunLoopGetCurrent();
            IntPtr mode = MacNative.CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", MacNative.Utf8StringEncoding);
            IntPtr source = MacNative.CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
            if (source == IntPtr.Zero || mode == IntPtr.Zero)
            {
                if (source != IntPtr.Zero) MacNative.CFRelease(source);
                if (mode != IntPtr.Zero) MacNative.CFRelease(mode);
                MacNative.CFRelease(tap);
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
            MacNative.CGEventTapEnable(tap, true);
            started.TrySetResult(null);
            MacNative.CFRunLoopRun();
        }
        catch (Exception exception)
        {
            started.TrySetResult(exception);
            Publish(new InputFilterRuntimeSnapshot(InputFilterRuntimeStatus.Faulted, Message: exception.Message));
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
        _diagnostics.Configure(configuration.Diagnostics);
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

    private void ReleaseTapResources()
    {
        lock (_sync)
        {
            if (_runLoop != IntPtr.Zero && _source != IntPtr.Zero && _runLoopMode != IntPtr.Zero)
            {
                MacNative.CFRunLoopRemoveSource(_runLoop, _source, _runLoopMode);
            }
            if (_source != IntPtr.Zero) MacNative.CFRelease(_source);
            if (_runLoopMode != IntPtr.Zero) MacNative.CFRelease(_runLoopMode);
            if (_tap != IntPtr.Zero) MacNative.CFRelease(_tap);
            _tap = _runLoop = _source = _runLoopMode = IntPtr.Zero;
            _thread = null;
        }
    }

    private static ulong EventMask(params nuint[] eventTypes) => eventTypes.Aggregate(0UL, static (mask, type) => mask | (1UL << (int)type));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
