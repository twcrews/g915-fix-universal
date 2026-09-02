namespace G915Fix.Core.Input;

/// <summary>
/// Configures keyboard debouncing using normalized HID Keyboard/Keypad usages.
/// </summary>
public sealed class KeyboardDebounceOptions
{
    public TimeSpan MinimumRepeatInterval { get; init; } = TimeSpan.FromMilliseconds(28);

    public KeyboardDebounceMode Mode { get; init; } = KeyboardDebounceMode.BlockRepress;

    public bool EnableBurstBypass { get; init; }

    public TimeSpan BurstGap { get; init; } = TimeSpan.FromMilliseconds(25);

    public int BurstMinimumStreak { get; init; } = 2;

    public IReadOnlySet<HidKeyboardUsage> ExcludedKeys { get; init; } =
        new HashSet<HidKeyboardUsage>();

    public IReadOnlyDictionary<HidKeyboardUsage, TimeSpan> PerKeyMinimumRepeatIntervals { get; init; } =
        new Dictionary<HidKeyboardUsage, TimeSpan>();
}
