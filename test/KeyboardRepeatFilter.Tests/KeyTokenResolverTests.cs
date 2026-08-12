using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardRepeatFilter.Tests
{
    [TestClass]
    public class KeyTokenResolverTests
    {
        [TestMethod]
        public void Resolve_ReturnsDecimalVirtualKeyCode()
        {
            CollectionAssert.AreEqual(new[] { 8 }, KeyTokenResolver.Resolve("8").ToArray());
        }

        [TestMethod]
        public void Resolve_ReturnsNamedVirtualKeyCode_WithOptionalPrefixAndCaseInsensitivity()
        {
            CollectionAssert.AreEqual(new[] { 13 }, KeyTokenResolver.Resolve("VK_RETURN").ToArray());
            CollectionAssert.AreEqual(new[] { 13 }, KeyTokenResolver.Resolve("return").ToArray());
        }

        [TestMethod]
        public void Resolve_ExpandsGenericModifiersToAllVariants()
        {
            CollectionAssert.AreEqual(new[] { 0x11, 0xA2, 0xA3 }, KeyTokenResolver.Resolve("Ctrl").ToArray());
            CollectionAssert.AreEqual(new[] { 0x10, 0xA0, 0xA1 }, KeyTokenResolver.Resolve("Shift").ToArray());
            CollectionAssert.AreEqual(new[] { 0x12, 0xA4, 0xA5 }, KeyTokenResolver.Resolve("Alt").ToArray());
        }

        [TestMethod]
        public void Resolve_ReturnsEmptyForUnknownOrOutOfRangeTokens()
        {
            Assert.AreEqual(0, KeyTokenResolver.Resolve("not-a-key").Count);
            Assert.AreEqual(0, KeyTokenResolver.Resolve("999").Count);
            Assert.AreEqual(0, KeyTokenResolver.Resolve(" ").Count);
        }
    }
}
