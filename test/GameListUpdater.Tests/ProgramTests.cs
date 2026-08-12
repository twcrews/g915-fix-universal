using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameListUpdater.Tests
{
    [TestClass]
    public class ProgramTests
    {
        [TestMethod]
        public void ExtractWin32Exes_ReturnsUniqueWin32FilenamesAndSkipsDenylistedHosts()
        {
            const string json = @"[
  {
    ""executables"": [
      { ""os"": ""win32"", ""name"": ""C:\\Games\\ExampleGame.exe"" },
      { ""os"": ""win32"", ""name"": ""/opt/games/AnotherGame.exe"" },
      { ""os"": ""win32"", ""name"": ""examplegame.exe"" },
      { ""os"": ""win32"", ""name"": ""dotnet.exe"" },
      { ""os"": ""linux"", ""name"": ""LinuxOnly"" },
      { ""os"": ""win32"", ""name"": ""   "" }
    ]
  },
  { ""executables"": null },
  { }
]";

            var exes = Program.ExtractWin32Exes(json);

            Assert.AreEqual(2, exes.Count);
            Assert.IsTrue(exes.Contains("ExampleGame.exe"));
            Assert.IsTrue(exes.Contains("AnotherGame.exe"));
            Assert.IsFalse(exes.Contains("dotnet.exe"));
            Assert.IsFalse(exes.Contains("LinuxOnly"));
        }

        [TestMethod]
        public void NormalizeBody_IgnoresHeadersBlankLinesAndCarriageReturns()
        {
            const string content = "# header\r\n\r\n game-a.exe \r\n# games=1\r\ngame-b.exe\r\n";

            string normalized = Program.NormalizeBody(content);

            Assert.AreEqual("game-a.exe\ngame-b.exe\n", normalized);
        }
    }
}
