using G915Fix.Core.Input;
using G915Fix.MacOS.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.MacOS.Tests;

[TestClass]
public sealed class MacKeyCodeMapTests
{
    [DataTestMethod]
    [DataRow((ushort)0, HidKeyboardUsage.A)]
    [DataRow((ushort)36, HidKeyboardUsage.Enter)]
    [DataRow((ushort)59, HidKeyboardUsage.LeftControl)]
    [DataRow((ushort)126, HidKeyboardUsage.UpArrow)]
    [DataRow((ushort)82, HidKeyboardUsage.Keypad0)]
    public void MapsMacHardwareCodesToHidUsages(ushort keyCode, HidKeyboardUsage expected)
    {
        Assert.IsTrue(MacKeyCodeMap.TryGetUsage(keyCode, out HidKeyboardUsage usage));
        Assert.AreEqual(expected, usage);
        Assert.IsTrue(MacKeyCodeMap.TryGetKeyCode(usage, out ushort reverse));
        Assert.AreEqual(keyCode, reverse);
    }

    [TestMethod]
    public void UnknownKeyCodeIsNotFiltered()
    {
        Assert.IsFalse(MacKeyCodeMap.TryGetUsage(63, out _)); // Fn has no portable HID usage in Core.
    }

    [TestMethod]
    public void FlagsChangedProducesCorrectModifierDirection()
    {
        Assert.IsTrue(MacKeyCodeMap.TryGetModifierKind(56, 1UL << 17, out KeyboardInputKind down));
        Assert.AreEqual(KeyboardInputKind.KeyDown, down);
        Assert.IsTrue(MacKeyCodeMap.TryGetModifierKind(56, 0, out KeyboardInputKind up));
        Assert.AreEqual(KeyboardInputKind.KeyUp, up);
        Assert.IsFalse(MacKeyCodeMap.TryGetModifierKind(0, 0, out _));
    }
}
