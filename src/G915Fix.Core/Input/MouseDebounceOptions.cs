namespace G915Fix.Core.Input;

/// <summary>Configures mouse-button debouncing.</summary>
public sealed class MouseDebounceOptions
{
    public TimeSpan MinimumRepeatInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    public IReadOnlySet<MouseButton> ExcludedButtons { get; init; } =
        new HashSet<MouseButton>();
}
