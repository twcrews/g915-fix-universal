#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
      { ""os"": ""win32"", ""name"": ""   "" },
      null,
      123
    ]
  },
  { ""executables"": null },
  { }
]";

            var exes = Program.ExtractWin32Exes(json);

            Assert.AreEqual(2, exes.Count);
            Assert.IsTrue(exes.Contains("examplegame.exe"));
            Assert.IsTrue(exes.Contains("anothergame.exe"));
            Assert.IsFalse(exes.Contains("dotnet.exe"));
            Assert.IsFalse(exes.Contains("LinuxOnly"));
        }

        [TestMethod]
        public void ExtractExecutableNames_UsesRequestedOsFilter()
        {
            const string json = @"[
  {
    ""executables"": [
      { ""os"": ""win32"", ""name"": ""Game.exe"" },
      { ""os"": ""linux"", ""name"": ""/usr/bin/LinuxGame"" },
      { ""os"": ""darwin"", ""name"": ""/Applications/MacGame.app/MacGame"" },
      { ""name"": ""MissingOsShouldNotMatchAll"" }
    ]
  }
]";

            var linux = Program.ExtractExecutableNames(json, "linux");
            var all = Program.ExtractExecutableNames(json, "all");

            CollectionAssert.AreEquivalent(new[] { "linuxgame" }, linux);
            CollectionAssert.AreEquivalent(new[] { "game.exe", "linuxgame", "macgame" }, all);
        }

        [TestMethod]
        [ExpectedException(typeof(JsonException))]
        public void ExtractExecutableNames_ThrowsWhenRootIsNotArray()
        {
            Program.ExtractWin32Exes("{}");
        }

        [TestMethod]
        public void NormalizeBody_IgnoresHeadersBlankLinesAndCarriageReturns()
        {
            const string content = "# header\r\n\r\n game-a.exe \r\n# games=1\r\ngame-b.exe\r\n";

            string normalized = Program.NormalizeBody(content);

            Assert.AreEqual("game-a.exe\ngame-b.exe\n", normalized);
        }

        [TestMethod]
        public void BuildContent_IncludesOsAndGameCountHeaders()
        {
            string content = GameListWriter.BuildContent(new[] { "a.exe", "b.exe" }, "win32");

            StringAssert.Contains(content, "# Discord detectable-games executable list.");
            StringAssert.Contains(content, "os=win32 games=2");
            StringAssert.EndsWith(content, "a.exe\nb.exe\n");
        }

        [TestMethod]
        public void Options_DefaultToWin32GamesTxtAndHttpCache()
        {
            bool parsed = GameListUpdaterOptions.TryParse([], out GameListUpdaterOptions options, out string? error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual("win32", options.OsFilter);
            Assert.AreEqual("games.txt", Path.GetFileName(options.OutputPath));
            Assert.AreEqual(options.OutputPath + ".httpcache.json", options.CachePath);
            Assert.AreEqual(3, options.MaxAttempts);
            Assert.AreEqual(45, options.TimeoutSeconds);
        }

        [TestMethod]
        public void Options_ParseCrossPlatformOutputAndDisableCache()
        {
            string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "custom-games.txt");

            bool parsed = GameListUpdaterOptions.TryParse(
                ["--os", "linux", "--output", output, "--no-cache", "--timeout", "10", "--retries", "1"],
                out GameListUpdaterOptions options,
                out string? error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual("linux", options.OsFilter);
            Assert.AreEqual(Path.GetFullPath(output), options.OutputPath);
            Assert.IsNull(options.CachePath);
            Assert.AreEqual(10, options.TimeoutSeconds);
            Assert.AreEqual(1, options.MaxAttempts);
        }

        [TestMethod]
        public async Task DetectableGamesClient_SendsCacheValidatorsAndHandlesNotModified()
        {
            HttpCacheMetadata cache = new("\"abc\"", "Wed, 21 Oct 2015 07:28:00 GMT");
            using HttpClient httpClient = new(new DelegateHandler(request =>
            {
                Assert.AreEqual("\"abc\"", request.Headers.IfNoneMatch.ToString());
                Assert.AreEqual(DateTimeOffset.Parse(cache.LastModified!), request.Headers.IfModifiedSince);
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }))
            {
                BaseAddress = new Uri("https://example.invalid/")
            };

            DetectableGamesClient client = new(httpClient, maxAttempts: 1);

            await using DetectableGamesDownload download = await client.DownloadAsync(cache);

            Assert.IsTrue(download.NotModified);
            Assert.IsNull(download.Content);
        }

        [TestMethod]
        public async Task DetectableGamesClient_RetriesRetryableStatusAndCapturesCacheMetadata()
        {
            int calls = 0;
            using HttpClient httpClient = new(new DelegateHandler(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    HttpResponseMessage retry = new(HttpStatusCode.TooManyRequests);
                    retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                    return retry;
                }

                HttpResponseMessage ok = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                ok.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fresh\"");
                ok.Content.Headers.LastModified = DateTimeOffset.Parse("Wed, 21 Oct 2015 07:28:00 GMT");
                return ok;
            }))
            {
                BaseAddress = new Uri("https://example.invalid/")
            };

            DetectableGamesClient client = new(httpClient, maxAttempts: 2);

            await using DetectableGamesDownload download = await client.DownloadAsync(null);

            Assert.AreEqual(2, calls);
            Assert.IsFalse(download.NotModified);
            Assert.IsNotNull(download.Content);
            Assert.AreEqual("\"fresh\"", download.CacheMetadata?.ETag);
        }

        [TestMethod]
        public void WriteAtomic_CreatesParentDirectoryAndReplacesContent()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "games.txt");
            try
            {
                Program.WriteAtomic(path, "old\n");
                Program.WriteAtomic(path, "new\n");

                Assert.AreEqual("new\n", File.ReadAllText(path));
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class DelegateHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

            public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            {
                _send = send;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_send(request));
        }
    }
}
