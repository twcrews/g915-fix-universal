namespace G915Fix.Core.Input;

/// <summary>
/// A normalized keyboard event. Native hooks must map their platform-specific
/// key codes to <see cref="HidKeyboardUsage"/> before creating this event.
/// When supplied, <see cref="Timestamp"/> must use the same monotonic tick
/// source and frequency configured on the receiving filter.
/// </summary>
public readonly record struct KeyboardInputEvent(
    HidKeyboardUsage Key,
    KeyboardInputKind Kind,
    bool IsInjected = false,
    long? Timestamp = null);
