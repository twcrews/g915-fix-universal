namespace G915Fix.Core.Input;

/// <summary>
/// A mouse-button event. When supplied, <see cref="Timestamp"/> must use the
/// same monotonic tick source and frequency configured on the receiving filter.
/// </summary>
public readonly record struct MouseInputEvent(
    MouseButton Button,
    MouseInputKind Kind,
    long? Timestamp = null);
