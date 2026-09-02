using System.Net;
using System.Net.Http.Headers;
using G915Fix.Core.Games;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class GameListUpdaterTests
{
    [TestMethod]
    public async Task UpdateAsync_ParsesAndStoresRequestedPlatformGames()
    {
        var store = new MemoryGameListStore();
        using var client = new HttpClient(new DelegateHandler(_ => JsonResponse("""
            [{"executables":[
              {"os":"win32","name":"C:\\Games\\Example.exe"},
              {"os":"win32","name":"dotnet.exe"},
              {"os":"linux","name":"linux-only"},
              {"os":"win32","name":"example.exe"}
            ]}]
            """)));
        var updater = new DiscordGameListUpdater(client, store, CreateOptions());

        GameListUpdateResult result = await updater.UpdateAsync();

        Assert.AreEqual(GameListUpdateStatus.Updated, result.Status);
        CollectionAssert.AreEquivalent(new[] { "example.exe" }, result.ExecutableNames.ToArray());
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task UpdateAsync_UsesValidatorsAndPreservesStoredListOnNotModified()
    {
        var store = new MemoryGameListStore(["existing.exe"]);
        var cache = new MemoryCacheStore(new GameListCacheMetadata("\"cached\"", DateTimeOffset.Parse("2025-01-01T00:00:00Z")));
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            Assert.AreEqual("\"cached\"", request.Headers.IfNoneMatch.ToString());
            Assert.AreEqual(DateTimeOffset.Parse("2025-01-01T00:00:00Z"), request.Headers.IfModifiedSince);
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }));
        var updater = new DiscordGameListUpdater(client, store, CreateOptions(), cache);

        GameListUpdateResult result = await updater.UpdateAsync();

        Assert.AreEqual(GameListUpdateStatus.UpToDate, result.Status);
        Assert.IsTrue(result.WasNotModified);
        CollectionAssert.AreEquivalent(new[] { "existing.exe" }, result.ExecutableNames.ToArray());
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task UpdateAsync_DoesNotOverwriteListWhenResponseHasNoUsableGames()
    {
        var store = new MemoryGameListStore(["existing.exe"]);
        using var client = new HttpClient(new DelegateHandler(_ => JsonResponse("[]")));
        var updater = new DiscordGameListUpdater(client, store, CreateOptions());

        GameListUpdateResult result = await updater.UpdateAsync();

        Assert.AreEqual(GameListUpdateStatus.Failed, result.Status);
        CollectionAssert.AreEquivalent(new[] { "existing.exe" }, store.Games.ToArray());
        Assert.AreEqual(0, store.SaveCount);
    }

    private static GameListUpdateOptions CreateOptions() => new()
    {
        ApiUri = new Uri("https://example.invalid/detectable"),
        Platform = GameListPlatform.Windows,
        MaxAttempts = 1
    };

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class MemoryGameListStore(IEnumerable<string>? games = null) : IGameListStore
    {
        public HashSet<string> Games { get; private set; } = new(games ?? [], StringComparer.OrdinalIgnoreCase);
        public int SaveCount { get; private set; }

        public Task<IReadOnlySet<string>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(Games, StringComparer.OrdinalIgnoreCase));

        public Task SaveAsync(IReadOnlySet<string> executableNames, CancellationToken cancellationToken = default)
        {
            Games = new HashSet<string>(executableNames, StringComparer.OrdinalIgnoreCase);
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryCacheStore(GameListCacheMetadata? cache) : IGameListCacheStore
    {
        public Task<GameListCacheMetadata?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(cache);

        public Task SaveAsync(GameListCacheMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
