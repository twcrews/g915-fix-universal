using System.Net;
using System.Text;
using G915Fix.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class UpdateCheckerTests
{
    [TestMethod]
    public async Task CheckAsync_ReturnsUpdateAndConstructsTrustedReleaseUri()
    {
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            Assert.IsTrue(request.Headers.UserAgent.Any());
            return JsonResponse("""{"tag_name":"v2.3.4","html_url":"https://untrusted.example/release"}""");
        }));
        var checker = new GitHubReleaseUpdateChecker(client);

        UpdateCheckResult result = await checker.CheckAsync(new Version(2, 0, 0));

        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual(new Version(2, 3, 4), result.LatestVersion);
        Assert.AreEqual("https://github.com/twcrews/g915-fix-universal/releases/tag/v2.3.4", result.ReleaseUri?.ToString());
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsUpToDateForEquivalentVersions()
    {
        using var client = new HttpClient(new DelegateHandler(_ => JsonResponse("""{"tag_name":"2.3"}""")));
        var checker = new GitHubReleaseUpdateChecker(client);

        UpdateCheckResult result = await checker.CheckAsync(new Version(2, 3, 0, 9));

        Assert.AreEqual(UpdateCheckStatus.UpToDate, result.Status);
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsFailedForInvalidResponses()
    {
        using var client = new HttpClient(new DelegateHandler(_ => JsonResponse("""{"tag_name":"release-two"}""")));
        var checker = new GitHubReleaseUpdateChecker(client);

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0));

        Assert.AreEqual(UpdateCheckStatus.Failed, result.Status);
        Assert.IsNull(result.LatestVersion);
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsFailedForHttpErrors()
    {
        using var client = new HttpClient(new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var checker = new GitHubReleaseUpdateChecker(client);

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0));

        Assert.AreEqual(UpdateCheckStatus.Failed, result.Status);
        StringAssert.Contains(result.Message, "429");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
