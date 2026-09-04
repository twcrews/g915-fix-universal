using G915Fix.Core.Input;

namespace G915Fix.Core.Configuration;

/// <summary>Converts bindable configuration into normalized filter options.</summary>
public sealed class ConfigurationCompiler
{
    private readonly IKeyboardTokenResolver _keyboardTokenResolver;

    public ConfigurationCompiler(IKeyboardTokenResolver? keyboardTokenResolver = null)
    {
        _keyboardTokenResolver = keyboardTokenResolver ?? new HidKeyboardTokenResolver();
    }

    public ConfigurationCompilationResult Compile(AppConfiguration? configuration)
    {
        configuration ??= new AppConfiguration();
        var warnings = new List<ConfigurationWarning>();
        KeyboardFilterConfiguration keyboard = configuration.Keyboard ?? new KeyboardFilterConfiguration();
        MouseFilterConfiguration mouse = configuration.Mouse ?? new MouseFilterConfiguration();

        var excludedKeys = new HashSet<HidKeyboardUsage>();
        foreach (string? token in keyboard.ExcludedKeys ?? [])
        {
            IReadOnlyList<HidKeyboardUsage> usages = _keyboardTokenResolver.Resolve(token ?? string.Empty);
            if (usages.Count == 0)
            {
                warnings.Add(new ConfigurationWarning("Keyboard.ExcludedKeys", $"'{token}' is not a recognized HID key token."));
                continue;
            }

            excludedKeys.UnionWith(usages);
        }

        var perKeyIntervals = new Dictionary<HidKeyboardUsage, TimeSpan>();
        foreach ((string token, double milliseconds) in keyboard.PerKeyMinimumRepeatIntervalMs ?? [])
        {
            if (!IsNonNegativeFinite(milliseconds))
            {
                warnings.Add(new ConfigurationWarning($"Keyboard.PerKeyMinimumRepeatIntervalMs.{token}", "The interval must be a non-negative finite number."));
                continue;
            }

            IReadOnlyList<HidKeyboardUsage> usages = _keyboardTokenResolver.Resolve(token);
            if (usages.Count == 0)
            {
                warnings.Add(new ConfigurationWarning("Keyboard.PerKeyMinimumRepeatIntervalMs", $"'{token}' is not a recognized HID key token."));
                continue;
            }

            foreach (HidKeyboardUsage usage in usages)
            {
                perKeyIntervals[usage] = TimeSpan.FromMilliseconds(milliseconds);
            }
        }

        TimeSpan minimumKeyboardInterval = GetDuration(
            keyboard.MinimumRepeatIntervalMs,
            28,
            "Keyboard.MinimumRepeatIntervalMs",
            warnings);
        TimeSpan burstGap = GetDuration(keyboard.BurstGapMs, 25, "Keyboard.BurstGapMs", warnings);
        int burstMinimumStreak = keyboard.BurstMinimumStreak;
        if (burstMinimumStreak < 1)
        {
            warnings.Add(new ConfigurationWarning("Keyboard.BurstMinimumStreak", "The value must be at least 1; the default value was used."));
            burstMinimumStreak = 2;
        }

        var excludedButtons = new HashSet<MouseButton>();
        foreach (string? token in mouse.ExcludedButtons ?? [])
        {
            if (!TryResolveMouseButton(token, out MouseButton button))
            {
                warnings.Add(new ConfigurationWarning("Mouse.ExcludedButtons", $"'{token}' is not a recognized mouse button."));
                continue;
            }

            excludedButtons.Add(button);
        }

        TimeSpan minimumMouseInterval = GetDuration(
            mouse.MinimumRepeatIntervalMs,
            50,
            "Mouse.MinimumRepeatIntervalMs",
            warnings);

        return new ConfigurationCompilationResult(
            new KeyboardDebounceOptions
            {
                Mode = keyboard.Mode,
                MinimumRepeatInterval = minimumKeyboardInterval,
                EnableBurstBypass = keyboard.BurstBypass,
                BurstGap = burstGap,
                BurstMinimumStreak = burstMinimumStreak,
                ExcludedKeys = excludedKeys,
                PerKeyMinimumRepeatIntervals = perKeyIntervals
            },
            new MouseDebounceOptions
            {
                MinimumRepeatInterval = minimumMouseInterval,
                ExcludedButtons = excludedButtons
            },
            keyboard.Enabled,
            mouse.Enabled,
            warnings);
    }

    private static TimeSpan GetDuration(
        double milliseconds,
        double defaultMilliseconds,
        string path,
        ICollection<ConfigurationWarning> warnings)
    {
        if (IsNonNegativeFinite(milliseconds) && milliseconds <= TimeSpan.MaxValue.TotalMilliseconds)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        warnings.Add(new ConfigurationWarning(path, "The interval must be a non-negative finite duration; the default value was used."));
        return TimeSpan.FromMilliseconds(defaultMilliseconds);
    }

    private static bool IsNonNegativeFinite(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool TryResolveMouseButton(string? token, out MouseButton button)
    {
        string normalized = string.Concat((token ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
        button = normalized switch
        {
            "LEFT" or "LBUTTON" => MouseButton.Left,
            "RIGHT" or "RBUTTON" => MouseButton.Right,
            "MIDDLE" or "MBUTTON" => MouseButton.Middle,
            "X1" or "XBUTTON1" => MouseButton.X1,
            "X2" or "XBUTTON2" => MouseButton.X2,
            _ => default
        };
        return normalized is "LEFT" or "LBUTTON" or "RIGHT" or "RBUTTON" or "MIDDLE" or "MBUTTON" or "X1" or "XBUTTON1" or "X2" or "XBUTTON2";
    }
}

public sealed record ConfigurationWarning(string Path, string Message);

public sealed record ConfigurationCompilationResult(
    KeyboardDebounceOptions KeyboardOptions,
    MouseDebounceOptions MouseOptions,
    bool KeyboardEnabled,
    bool MouseEnabled,
    IReadOnlyList<ConfigurationWarning> Warnings);
