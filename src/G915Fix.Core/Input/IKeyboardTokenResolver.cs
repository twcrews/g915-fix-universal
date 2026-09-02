namespace G915Fix.Core.Input;

/// <summary>
/// Resolves a user-facing keyboard configuration token to normalized HID
/// Keyboard/Keypad usages. Implementations may support platform-specific or
/// legacy token aliases, but must never return native key codes.
/// </summary>
public interface IKeyboardTokenResolver
{
    IReadOnlyList<HidKeyboardUsage> Resolve(string token);
}
