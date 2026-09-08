using G915Fix.Core.Input;

namespace G915Fix.MacOS.Input;

/// <summary>Maps macOS hardware virtual key codes to USB HID keyboard usages.</summary>
internal static class MacKeyCodeMap
{
    private static readonly IReadOnlyDictionary<ushort, HidKeyboardUsage> ByKeyCode =
        new Dictionary<ushort, HidKeyboardUsage>
        {
            [0] = HidKeyboardUsage.A, [1] = HidKeyboardUsage.S, [2] = HidKeyboardUsage.D,
            [3] = HidKeyboardUsage.F, [4] = HidKeyboardUsage.H, [5] = HidKeyboardUsage.G,
            [6] = HidKeyboardUsage.Z, [7] = HidKeyboardUsage.X, [8] = HidKeyboardUsage.C,
            [9] = HidKeyboardUsage.V, [11] = HidKeyboardUsage.B, [12] = HidKeyboardUsage.Q,
            [13] = HidKeyboardUsage.W, [14] = HidKeyboardUsage.E, [15] = HidKeyboardUsage.R,
            [16] = HidKeyboardUsage.Y, [17] = HidKeyboardUsage.T, [18] = HidKeyboardUsage.Number1,
            [19] = HidKeyboardUsage.Number2, [20] = HidKeyboardUsage.Number3, [21] = HidKeyboardUsage.Number4,
            [22] = HidKeyboardUsage.Number6, [23] = HidKeyboardUsage.Number5, [24] = HidKeyboardUsage.Equal,
            [25] = HidKeyboardUsage.Number9, [26] = HidKeyboardUsage.Number7, [27] = HidKeyboardUsage.Minus,
            [28] = HidKeyboardUsage.Number8, [29] = HidKeyboardUsage.Number0, [30] = HidKeyboardUsage.RightBracket,
            [31] = HidKeyboardUsage.O, [32] = HidKeyboardUsage.U, [33] = HidKeyboardUsage.LeftBracket,
            [34] = HidKeyboardUsage.I, [35] = HidKeyboardUsage.P, [36] = HidKeyboardUsage.Enter,
            [37] = HidKeyboardUsage.L, [38] = HidKeyboardUsage.J, [39] = HidKeyboardUsage.Apostrophe,
            [40] = HidKeyboardUsage.K, [41] = HidKeyboardUsage.Semicolon, [42] = HidKeyboardUsage.Backslash,
            [43] = HidKeyboardUsage.Comma, [44] = HidKeyboardUsage.Slash, [45] = HidKeyboardUsage.N,
            [46] = HidKeyboardUsage.M, [47] = HidKeyboardUsage.Period, [48] = HidKeyboardUsage.Tab,
            [49] = HidKeyboardUsage.Space, [50] = HidKeyboardUsage.Grave, [51] = HidKeyboardUsage.Backspace,
            [53] = HidKeyboardUsage.Escape, [55] = HidKeyboardUsage.LeftGui, [56] = HidKeyboardUsage.LeftShift,
            [57] = HidKeyboardUsage.CapsLock, [58] = HidKeyboardUsage.LeftAlt, [59] = HidKeyboardUsage.LeftControl,
            [60] = HidKeyboardUsage.RightShift, [61] = HidKeyboardUsage.RightAlt, [62] = HidKeyboardUsage.RightControl,
            [64] = HidKeyboardUsage.F17, [65] = HidKeyboardUsage.KeypadDecimal, [67] = HidKeyboardUsage.KeypadMultiply,
            [69] = HidKeyboardUsage.KeypadAdd, [71] = HidKeyboardUsage.NumLock, [75] = HidKeyboardUsage.KeypadDivide,
            [76] = HidKeyboardUsage.KeypadEnter, [78] = HidKeyboardUsage.KeypadSubtract, [79] = HidKeyboardUsage.F18,
            [80] = HidKeyboardUsage.F19, [81] = HidKeyboardUsage.KeypadEqual, [82] = HidKeyboardUsage.Keypad0,
            [83] = HidKeyboardUsage.Keypad1, [84] = HidKeyboardUsage.Keypad2, [85] = HidKeyboardUsage.Keypad3,
            [86] = HidKeyboardUsage.Keypad4, [87] = HidKeyboardUsage.Keypad5, [88] = HidKeyboardUsage.Keypad6,
            [89] = HidKeyboardUsage.Keypad7, [91] = HidKeyboardUsage.Keypad8, [92] = HidKeyboardUsage.Keypad9,
            [96] = HidKeyboardUsage.F5, [97] = HidKeyboardUsage.F6, [98] = HidKeyboardUsage.F7,
            [99] = HidKeyboardUsage.F3, [100] = HidKeyboardUsage.F8, [101] = HidKeyboardUsage.F9,
            [103] = HidKeyboardUsage.F11, [105] = HidKeyboardUsage.F13, [106] = HidKeyboardUsage.F16,
            [107] = HidKeyboardUsage.F14, [109] = HidKeyboardUsage.F10, [111] = HidKeyboardUsage.F12,
            [113] = HidKeyboardUsage.F15, [114] = HidKeyboardUsage.Help, [115] = HidKeyboardUsage.Home,
            [116] = HidKeyboardUsage.PageUp, [117] = HidKeyboardUsage.Delete, [118] = HidKeyboardUsage.F4,
            [119] = HidKeyboardUsage.End, [120] = HidKeyboardUsage.F2, [121] = HidKeyboardUsage.PageDown,
            [122] = HidKeyboardUsage.F1, [123] = HidKeyboardUsage.LeftArrow, [124] = HidKeyboardUsage.RightArrow,
            [125] = HidKeyboardUsage.DownArrow, [126] = HidKeyboardUsage.UpArrow
        };

    private static readonly IReadOnlyDictionary<HidKeyboardUsage, ushort> ByUsage =
        ByKeyCode.ToDictionary(static pair => pair.Value, static pair => pair.Key);

    public static bool TryGetUsage(ushort keyCode, out HidKeyboardUsage usage) => ByKeyCode.TryGetValue(keyCode, out usage);

    public static bool TryGetKeyCode(HidKeyboardUsage usage, out ushort keyCode) => ByUsage.TryGetValue(usage, out keyCode);

    public static bool TryGetModifierKind(ushort keyCode, ulong flags, out KeyboardInputKind kind)
    {
        const ulong shift = 1UL << 17;
        const ulong control = 1UL << 18;
        const ulong option = 1UL << 19;
        const ulong command = 1UL << 20;
        ulong mask = keyCode switch
        {
            55 => command, 56 or 60 => shift, 58 or 61 => option, 59 or 62 => control,
            _ => 0
        };

        kind = (flags & mask) != 0 ? KeyboardInputKind.KeyDown : KeyboardInputKind.KeyUp;
        return mask != 0;
    }
}
