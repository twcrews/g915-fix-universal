namespace G915Fix.Core.Input;

public readonly record struct KeyboardInputEvent(
    int KeyCode,
    KeyboardInputKind Kind,
    uint ScanCode = 0,
    bool IsExtended = false,
    bool IsInjected = false,
    nuint ExtraInfo = 0,
    long? Timestamp = null);
