using System.Diagnostics;
using G915Fix.Core.Diagnostics;

namespace G915Fix.Core.Input;

/// <summary>
/// Platform-neutral mouse-button debounce state machine. A native input adapter
/// must invoke <see cref="ShouldSuppress"/> synchronously before delivering an event.
/// </summary>
public sealed class MouseDebounceFilter : IMouseInputFilter
{
    private readonly object _sync = new();
    private readonly Dictionary<MouseButton, ButtonState> _states = [];
    private readonly HashSet<MouseButton> _excludedButtons;
    private readonly Func<long> _getTimestamp;
    private readonly long _threshold;
    private readonly IFilterDiagnosticSink? _diagnosticSink;

    public MouseDebounceFilter(
        MouseDebounceOptions options,
        Func<long>? getTimestamp = null,
        double? timestampFrequency = null,
        IFilterDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MinimumRepeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MinimumRepeatInterval));
        }

        double frequency = timestampFrequency ?? Stopwatch.Frequency;
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _excludedButtons = new HashSet<MouseButton>(options.ExcludedButtons);
        _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        _diagnosticSink = diagnosticSink;
        _threshold = checked((long)Math.Ceiling(options.MinimumRepeatInterval.TotalSeconds * frequency));
    }

    public bool ShouldSuppress(MouseInputEvent inputEvent)
    {
        bool suppress;
        lock (_sync)
        {
            if (_excludedButtons.Contains(inputEvent.Button))
            {
                return false;
            }

            long now = inputEvent.Timestamp ?? _getTimestamp();
            ButtonState state = GetState(inputEvent.Button);
            if (inputEvent.Kind == MouseInputKind.ButtonDown)
            {
                suppress = !state.IsPressed
                    && state.HasLastUpTimestamp
                    && now - state.LastUpTimestamp < _threshold;
                state.IsPressed = !suppress;
            }
            else
            {
                state.LastUpTimestamp = now;
                state.HasLastUpTimestamp = true;
                state.IsPressed = false;
                suppress = false;
            }
        }

        if (suppress)
        {
            RecordDiagnostic(inputEvent.Button);
        }

        return suppress;
    }

    private void RecordDiagnostic(MouseButton button)
    {
        if (_diagnosticSink is null)
        {
            return;
        }

        try
        {
            _diagnosticSink.Record(new FilterDiagnosticEvent(
                FilterDiagnosticEvent.CurrentSchemaVersion,
                DateTimeOffset.UtcNow,
                FilterDiagnosticEventKind.MouseFiltered,
                MouseButton: button,
                Action: FilterDiagnosticAction.MousePressBlocked));
        }
        catch
        {
            // Diagnostics must never alter filtering behavior.
        }
    }

    private ButtonState GetState(MouseButton button)
    {
        if (!_states.TryGetValue(button, out ButtonState? state))
        {
            state = new ButtonState();
            _states.Add(button, state);
        }

        return state;
    }

    private sealed class ButtonState
    {
        public long LastUpTimestamp { get; set; }
        public bool HasLastUpTimestamp { get; set; }
        public bool IsPressed { get; set; }
    }
}
