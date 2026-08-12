using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardHeatmap.Tests
{
    [TestClass]
    public class MouseMapTests
    {
        [TestMethod]
        public void Parse_LoadsTopAndSideButtons()
        {
            const string json = @"{
  ""topImage"": ""mouse-top.png"",
  ""topWidth"": 500,
  ""topHeight"": 300,
  ""sideImage"": ""mouse-side.png"",
  ""sideWidth"": 400,
  ""sideHeight"": 200,
  ""buttons"": [
    { ""name"": ""Left"", ""image"": ""top"", ""x"": 1, ""y"": 2, ""width"": 3, ""height"": 4 },
    { ""name"": ""X1"", ""image"": ""side"", ""x"": 5, ""y"": 6, ""width"": 7, ""height"": 8 },
    { ""name"": ""Ignored"", ""image"": ""other"", ""x"": 9, ""y"": 10, ""width"": 11, ""height"": 12 }
  ]
}";

            var map = MouseMap.Parse(json);

            Assert.AreEqual("mouse-top.png", map.TopImage);
            Assert.AreEqual(500, map.TopWidth);
            Assert.AreEqual(300, map.TopHeight);
            Assert.AreEqual("mouse-side.png", map.SideImage);
            Assert.AreEqual(400, map.SideWidth);
            Assert.AreEqual(200, map.SideHeight);
            Assert.AreEqual(1, map.TopButtons.Count);
            Assert.AreEqual("Left", map.TopButtons[0].Name);
            Assert.AreEqual(1, map.SideButtons.Count);
            Assert.AreEqual("X1", map.SideButtons[0].Name);
        }

        [TestMethod]
        public void Parse_ThrowsWhenDimensionsOrButtonsAreMissing()
        {
            Assert.ThrowsException<InvalidDataException>(() => MouseMap.Parse(@"{ ""topWidth"": 0, ""topHeight"": 1, ""sideWidth"": 1, ""sideHeight"": 1 }"));
            Assert.ThrowsException<InvalidDataException>(() => MouseMap.Parse(@"{ ""topWidth"": 1, ""topHeight"": 1, ""sideWidth"": 1, ""sideHeight"": 1, ""buttons"": [] }"));
        }
    }
}
