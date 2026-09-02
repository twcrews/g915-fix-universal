using System.Globalization;
using System.Text.RegularExpressions;
using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;

namespace G915Fix.Heatmap;

/// <summary>
/// Imports the former Windows text-log format so historical reports remain
/// usable after diagnostics move to JSON Lines and HID usages.
/// </summary>
public static class LegacyWindowsLogImporter
{
    private static readonly Regex KeyboardFiltered = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - [A-Za-z0-9_]+=(?<code>\d+) (?<action>filtered|release-held)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MouseFiltered = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - Mouse_(?<button>[A-Za-z0-9]+) filtered$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConfigurationWarning = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+) - ConfigWarning: (?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEnumerable<FilterDiagnosticEvent> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (string line in lines)
        {
            if (TryParseLine(line, out FilterDiagnosticEvent? diagnosticEvent)
                && diagnosticEvent is not null)
            {
                yield return diagnosticEvent;
            }
        }
    }

    private static bool TryParseLine(string line, out FilterDiagnosticEvent? diagnosticEvent)
    {
        diagnosticEvent = null;
        Match match = KeyboardFiltered.Match(line);
        if (match.Success
            && TryParseTimestamp(match.Groups["timestamp"].Value, out DateTimeOffset timestamp)
            && int.TryParse(match.Groups["code"].Value, out int virtualKey)
            && TryMapVirtualKey(virtualKey, out HidKeyboardUsage key))
        {
            diagnosticEvent = new FilterDiagnosticEvent(
                FilterDiagnosticEvent.CurrentSchemaVersion,
                timestamp,
                FilterDiagnosticEventKind.KeyboardFiltered,
                Key: key,
                Action: match.Groups["action"].Value == "release-held"
                    ? FilterDiagnosticAction.ReleaseHeld
                    : FilterDiagnosticAction.RepressBlocked);
            return true;
        }

        match = MouseFiltered.Match(line);
        if (match.Success
            && TryParseTimestamp(match.Groups["timestamp"].Value, out timestamp)
            && TryMapMouseButton(match.Groups["button"].Value, out MouseButton button))
        {
            diagnosticEvent = new FilterDiagnosticEvent(
                FilterDiagnosticEvent.CurrentSchemaVersion,
                timestamp,
                FilterDiagnosticEventKind.MouseFiltered,
                MouseButton: button,
                Action: FilterDiagnosticAction.MousePressBlocked);
            return true;
        }

        match = ConfigurationWarning.Match(line);
        if (match.Success && TryParseTimestamp(match.Groups["timestamp"].Value, out timestamp))
        {
            diagnosticEvent = new FilterDiagnosticEvent(
                FilterDiagnosticEvent.CurrentSchemaVersion,
                timestamp,
                FilterDiagnosticEventKind.ConfigurationWarning,
                Message: match.Groups["message"].Value);
            return true;
        }

        return false;
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out DateTime local))
        {
            timestamp = new DateTimeOffset(local).ToUniversalTime();
            return true;
        }

        timestamp = default;
        return false;
    }

    private static bool TryMapMouseButton(string value, out MouseButton button)
    {
        string normalized = value.ToUpperInvariant();
        button = normalized switch
        {
            "LEFT" => MouseButton.Left,
            "RIGHT" => MouseButton.Right,
            "MIDDLE" => MouseButton.Middle,
            "X1" => MouseButton.X1,
            "X2" => MouseButton.X2,
            _ => default
        };
        return normalized is "LEFT" or "RIGHT" or "MIDDLE" or "X1" or "X2";
    }

    private static bool TryMapVirtualKey(int virtualKey, out HidKeyboardUsage usage)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            usage = (HidKeyboardUsage)(0x04 + virtualKey - 0x41);
            return true;
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            usage = (HidKeyboardUsage)(virtualKey == 0x30 ? 0x27 : 0x1E + virtualKey - 0x31);
            return true;
        }

        return VirtualKeyMap.TryGetValue(virtualKey, out usage);
    }

    private static readonly IReadOnlyDictionary<int, HidKeyboardUsage> VirtualKeyMap =
        new Dictionary<int, HidKeyboardUsage>
        {
            [0x08] = HidKeyboardUsage.Backspace, [0x09] = HidKeyboardUsage.Tab,
            [0x0D] = HidKeyboardUsage.Enter, [0x10] = HidKeyboardUsage.LeftShift,
            [0x11] = HidKeyboardUsage.LeftControl, [0x12] = HidKeyboardUsage.LeftAlt,
            [0x14] = HidKeyboardUsage.CapsLock,
            [0x1B] = HidKeyboardUsage.Escape, [0x20] = HidKeyboardUsage.Space,
            [0x21] = HidKeyboardUsage.PageUp, [0x22] = HidKeyboardUsage.PageDown,
            [0x23] = HidKeyboardUsage.End, [0x24] = HidKeyboardUsage.Home,
            [0x25] = HidKeyboardUsage.LeftArrow, [0x26] = HidKeyboardUsage.UpArrow,
            [0x27] = HidKeyboardUsage.RightArrow, [0x28] = HidKeyboardUsage.DownArrow,
            [0x2D] = HidKeyboardUsage.Insert, [0x2E] = HidKeyboardUsage.Delete,
            [0x70] = HidKeyboardUsage.F1, [0x71] = HidKeyboardUsage.F2,
            [0x72] = HidKeyboardUsage.F3, [0x73] = HidKeyboardUsage.F4,
            [0x74] = HidKeyboardUsage.F5, [0x75] = HidKeyboardUsage.F6,
            [0x76] = HidKeyboardUsage.F7, [0x77] = HidKeyboardUsage.F8,
            [0x78] = HidKeyboardUsage.F9, [0x79] = HidKeyboardUsage.F10,
            [0x7A] = HidKeyboardUsage.F11, [0x7B] = HidKeyboardUsage.F12,
            [0xA0] = HidKeyboardUsage.LeftShift, [0xA1] = HidKeyboardUsage.RightShift,
            [0xA2] = HidKeyboardUsage.LeftControl, [0xA3] = HidKeyboardUsage.RightControl,
            [0xA4] = HidKeyboardUsage.LeftAlt, [0xA5] = HidKeyboardUsage.RightAlt
        };
}
