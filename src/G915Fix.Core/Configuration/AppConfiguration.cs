using G915Fix.Core.Input;

namespace G915Fix.Core.Configuration;

/// <summary>
/// The portable, JSON-bindable application configuration. Native hosts supply
/// the file location; this model deliberately contains no OS-specific settings.
/// </summary>
public sealed class AppConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The profile to activate at the next launch; null selects the base configuration.</summary>
    public string? DefaultProfile { get; set; }

    public KeyboardFilterConfiguration Keyboard { get; set; } = new();

    public MouseFilterConfiguration Mouse { get; set; } = new();

    public DiagnosticsConfiguration Diagnostics { get; set; } = new();

    public UpdateConfiguration Updates { get; set; } = new();

    public GameProfileConfiguration Games { get; set; } = new();

    public NotificationConfiguration Notifications { get; set; } = new();
}

public sealed class KeyboardFilterConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>One of the portable <see cref="KeyboardDebounceMode"/> names.</summary>
    public string Mode { get; set; } = nameof(KeyboardDebounceMode.BlockRepress);

    public double MinimumRepeatIntervalMs { get; set; } = 28;

    public bool BurstBypass { get; set; }

    public double BurstGapMs { get; set; } = 25;

    public int BurstMinimumStreak { get; set; } = 2;

    /// <summary>HID usage names resolved by <see cref="IKeyboardTokenResolver"/>.</summary>
    public List<string> ExcludedKeys { get; set; } = ["Backspace", "Enter"];

    /// <summary>Per-HID-usage debounce windows in milliseconds.</summary>
    public Dictionary<string, double> PerKeyMinimumRepeatIntervalMs { get; set; } = [];
}

public sealed class MouseFilterConfiguration
{
    public bool Enabled { get; set; }

    public double MinimumRepeatIntervalMs { get; set; } = 50;

    public List<string> ExcludedButtons { get; set; } = [];
}

public sealed class DiagnosticsConfiguration
{
    public bool Enabled { get; set; }

    /// <summary>A host-resolved path. Hosts should choose a platform-appropriate default.</summary>
    public string? LogPath { get; set; }
}

public sealed class UpdateConfiguration
{
    public bool CheckForUpdates { get; set; } = true;
}

public sealed class GameProfileConfiguration
{
    public bool AutoSwitchProfiles { get; set; }

    public string? DefaultGameProfile { get; set; } = "gaming";

    /// <summary>Case-insensitive executable-name to profile-name mappings.</summary>
    public Dictionary<string, string> ProfileMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NotificationConfiguration
{
    public bool ShowAccessNotice { get; set; } = true;
}
