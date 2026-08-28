namespace G915Fix.Core.Input;

public readonly record struct MouseInputEvent(
    MouseButton Button,
    MouseInputKind Kind,
    long? Timestamp = null);
