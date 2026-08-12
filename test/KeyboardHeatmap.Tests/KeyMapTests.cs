using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyboardHeatmap.Tests
{
    [TestClass]
    public class KeyMapTests
    {
        [TestMethod]
        public void Parse_LoadsKeysAndFindsFirstBoxForVirtualKeyCode()
        {
            const string json = @"{
  ""image"": ""keyboard.png"",
  ""imageWidth"": 1000,
  ""imageHeight"": 400,
  ""keys"": [
    { ""name"": ""A"", ""vk"": 65, ""x"": 10, ""y"": 20, ""width"": 30, ""height"": 40 },
    { ""name"": ""DuplicateA"", ""vk"": 65, ""x"": 50, ""y"": 60, ""width"": 70, ""height"": 80 },
    { ""name"": ""G1"", ""vk"": null, ""x"": 90, ""y"": 100, ""width"": 20, ""height"": 20 }
  ]
}";

            var map = KeyMap.Parse(json);

            Assert.AreEqual("keyboard.png", map.Image);
            Assert.AreEqual(1000, map.ImageWidth);
            Assert.AreEqual(400, map.ImageHeight);
            Assert.AreEqual(3, map.Keys.Count);
            Assert.AreEqual("A", map.FindByVk(65).Name);
            Assert.IsNull(map.FindByVk(66));
        }

        [TestMethod]
        public void ScaleToWidth_ScalesCoordinatesAndLabelsByImageWidth()
        {
            const string json = @"{
  ""image"": ""keyboard.png"",
  ""imageWidth"": 100,
  ""imageHeight"": 50,
  ""keys"": [
    { ""name"": ""A"", ""vk"": 65, ""x"": 10, ""y"": 20, ""width"": 30, ""height"": 10, ""labelX"": 20, ""labelY"": 25 }
  ]
}";

            var scaled = KeyMap.Parse(json).ScaleToWidth(200);
            var key = scaled.FindByVk(65);

            Assert.AreEqual(200, scaled.ImageWidth);
            Assert.AreEqual(100, scaled.ImageHeight);
            Assert.AreEqual(20, key.X);
            Assert.AreEqual(40, key.Y);
            Assert.AreEqual(60, key.Width);
            Assert.AreEqual(20, key.Height);
            Assert.AreEqual(40, key.LabelX);
            Assert.AreEqual(50, key.LabelY);
        }

        [TestMethod]
        public void Parse_ThrowsWhenImageDimensionsOrKeysAreMissing()
        {
            Assert.ThrowsException<InvalidDataException>(() => KeyMap.Parse(@"{ ""imageWidth"": 0, ""imageHeight"": 1, ""keys"": [] }"));
            Assert.ThrowsException<InvalidDataException>(() => KeyMap.Parse(@"{ ""imageWidth"": 1, ""imageHeight"": 1, ""keys"": [] }"));
        }
    }
}
