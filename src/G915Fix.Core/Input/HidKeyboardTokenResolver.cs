using System.Collections.Frozen;

namespace G915Fix.Core.Input;

/// <summary>
/// Resolves canonical HID keyboard names and common user-facing aliases to HID
/// Keyboard/Keypad usages. Numeric Windows virtual-key values are intentionally
/// not supported; a platform compatibility layer must translate those values.
/// </summary>
public sealed class HidKeyboardTokenResolver : IKeyboardTokenResolver
{
    private static readonly FrozenDictionary<string, HidKeyboardUsage> Names = BuildNames();

    private static readonly HidKeyboardUsage[] ControlKeys =
    [
        HidKeyboardUsage.LeftControl,
        HidKeyboardUsage.RightControl
    ];

    private static readonly HidKeyboardUsage[] ShiftKeys =
    [
        HidKeyboardUsage.LeftShift,
        HidKeyboardUsage.RightShift
    ];

    private static readonly HidKeyboardUsage[] AltKeys =
    [
        HidKeyboardUsage.LeftAlt,
        HidKeyboardUsage.RightAlt
    ];

    public IReadOnlyList<HidKeyboardUsage> Resolve(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        string normalized = Normalize(token);
        return normalized switch
        {
            "CTRL" or "CONTROL" => [.. ControlKeys],
            "SHIFT" => [.. ShiftKeys],
            "ALT" or "OPTION" => [.. AltKeys],
            _ when Names.TryGetValue(normalized, out HidKeyboardUsage usage) => [usage],
            _ => []
        };
    }

    private static FrozenDictionary<string, HidKeyboardUsage> BuildNames()
    {
        var names = new Dictionary<string, HidKeyboardUsage>(StringComparer.Ordinal);
        foreach (HidKeyboardUsage usage in Enum.GetValues<HidKeyboardUsage>())
        {
            if (usage != HidKeyboardUsage.None)
            {
                names[Normalize(usage.ToString())] = usage;
            }
        }

        AddAliases(names, HidKeyboardUsage.Enter, "RETURN");
        AddAliases(names, HidKeyboardUsage.Escape, "ESC");
        AddAliases(names, HidKeyboardUsage.Backspace, "BACK");
        AddAliases(names, HidKeyboardUsage.Space, "SPACEBAR");
        AddAliases(names, HidKeyboardUsage.PageUp, "PGUP");
        AddAliases(names, HidKeyboardUsage.PageDown, "PGDN");
        AddAliases(names, HidKeyboardUsage.PrintScreen, "PRTSC", "PRINT");
        AddAliases(names, HidKeyboardUsage.LeftControl, "LCONTROL", "LCTRL");
        AddAliases(names, HidKeyboardUsage.RightControl, "RCONTROL", "RCTRL");
        AddAliases(names, HidKeyboardUsage.LeftAlt, "LALT");
        AddAliases(names, HidKeyboardUsage.RightAlt, "RALT", "ALTGR");
        AddAliases(names, HidKeyboardUsage.LeftGui, "LWIN", "LCMD", "LCOMMAND", "LSUPER");
        AddAliases(names, HidKeyboardUsage.RightGui, "RWIN", "RCMD", "RCOMMAND", "RSUPER");

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void AddAliases(
        IDictionary<string, HidKeyboardUsage> names,
        HidKeyboardUsage usage,
        params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            names[Normalize(alias)] = usage;
        }
    }

    private static string Normalize(string token)
    {
        token = token.Trim();
        if (token.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
        {
            token = token[3..];
        }

        return string.Concat(token.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
