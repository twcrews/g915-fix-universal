using System.Diagnostics;

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

    public MouseDebounceFilter(
        MouseDebounceOptions options,
        Func<long>? getTimestamp = null,
        double? timestampFrequency = null)
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
        _threshold = checked((long)Math.Ceiling(options.MinimumRepeatInterval.TotalSeconds * frequency));
    }

    public bool ShouldSuppress(MouseInputEvent inputEvent)
    {
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
                if (!state.IsPressed
                    && state.HasLastUpTimestamp
                    && now - state.LastUpTimestamp < _threshold)
                {
                    return true;
                }

                state.IsPressed = true;
                return false;
            }

            state.LastUpTimestamp = now;
            state.HasLastUpTimestamp = true;
            state.IsPressed = false;
            return false;
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
