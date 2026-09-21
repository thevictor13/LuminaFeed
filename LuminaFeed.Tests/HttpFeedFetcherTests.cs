using System.Net;
using System.Text;
using LuminaFeed.Services.Polling;

namespace LuminaFeed.Tests;

public class HttpFeedFetcherTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return respond(request, cancellationToken);
        }
    }

    private static (HttpFeedFetcher Fetcher, StubHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler);
        HttpFeedFetcher.Configure(client);
        return (new HttpFeedFetcher(client), handler);
    }

    [Fact]
    public async Task FetchAsync_Success_ReturnsTheRawBody_AndSendsTheConfiguredHeaders()
    {
        var body = Encoding.UTF8.GetBytes("<rss/>");
        var (fetcher, handler) = Create((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));

        var result = await fetcher.FetchAsync("https://feeds.example.test/rss.xml");

        Assert.False(result.IsError);
        Assert.Equal(body, result.Value);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://feeds.example.test/rss.xml", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("LuminaFeed", handler.LastRequest.Headers.UserAgent.ToString());
        Assert.Contains(handler.LastRequest.Headers.Accept, a => a.MediaType == "application/rss+xml");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task FetchAsync_NonSuccessStatus_IsAnError(HttpStatusCode status)
    {
        var (fetcher, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        var result = await fetcher.FetchAsync("https://feeds.example.test/rss.xml");

        Assert.True(result.IsError);
        Assert.Equal("Polling.HttpStatus", result.FirstError.Code);
        Assert.Contains(((int)status).ToString(), result.FirstError.Description);
    }

    [Theory]
    [InlineData("http://feeds.example.test/feeds.xml", "https://feeds.example.test/feeds.xml")] // downgrade → upgraded
    [InlineData("http://feeds.example.test:8080/feeds.xml", "https://feeds.example.test:8080/feeds.xml")]
    [InlineData("/moved/feeds.xml", "https://feeds.example.test/moved/feeds.xml")] // relative
    [InlineData("https://cdn.example.test/feeds.xml", "https://cdn.example.test/feeds.xml")]
    public async Task FetchAsync_RedirectTheHandlerDidNotFollow_IsFollowedOverHttpsOnly(string location, string expectedUrl)
    {
        var requested = new List<string>();
        var (fetcher, _) = Create((request, _) =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            if (requested.Count == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { RequestMessage = request };
                redirect.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        });

        var result = await fetcher.FetchAsync("https://feeds.example.test/feeds/all");

        Assert.False(result.IsError);
        Assert.Equal([1, 2, 3], result.Value);
        Assert.Equal(["https://feeds.example.test/feeds/all", expectedUrl], requested);
        Assert.All(requested, url => Assert.StartsWith("https://", url));
    }

    [Fact]
    public async Task FetchAsync_RedirectLoop_GivesUpWithAnError()
    {
        var requests = 0;
        var (fetcher, _) = Create((request, _) =>
        {
            requests++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
            redirect.Headers.Location = new Uri("http://feeds.example.test/loop");
            return Task.FromResult(redirect);
        });

        var result = await fetcher.FetchAsync("https://feeds.example.test/loop");

        Assert.True(result.IsError);
        Assert.Equal("Polling.HttpStatus", result.FirstError.Code);
        Assert.Equal(HttpFeedFetcher.MaxManualRedirects + 1, requests);
    }

    [Fact]
    public async Task FetchAsync_RedirectWithoutLocation_IsAnError()
    {
        var (fetcher, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MovedPermanently)));

        var result = await fetcher.FetchAsync("https://feeds.example.test/rss.xml");

        Assert.True(result.IsError);
        Assert.Contains("301", result.FirstError.Description);
    }

    [Fact]
    public async Task FetchAsync_TransportFailure_IsAnError_NotAnException()
    {
        var (fetcher, _) = Create((_, _) => throw new HttpRequestException("connection refused"));

        var result = await fetcher.FetchAsync("https://feeds.example.test/rss.xml");

        Assert.True(result.IsError);
        Assert.Equal("Polling.FetchFailed", result.FirstError.Code);
        Assert.Contains("connection refused", result.FirstError.Description);
    }

    [Fact]
    public async Task FetchAsync_Timeout_IsAnError()
    {
        // HttpClient surfaces its own timeout as a TaskCanceledException the caller didn't ask for.
        var (fetcher, _) = Create((_, _) => throw new TaskCanceledException("timed out"));

        var result = await fetcher.FetchAsync("https://feeds.example.test/rss.xml");

        Assert.True(result.IsError);
        Assert.Equal("Polling.FetchFailed", result.FirstError.Code);
    }

    [Fact]
    public async Task FetchAsync_CallerCancellation_Propagates()
    {
        var (fetcher, _) = Create(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.FetchAsync("https://feeds.example.test/rss.xml", cts.Token));
    }

    [Fact]
    public async Task FetchAsync_InvalidUrl_IsAnError()
    {
        var (fetcher, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await fetcher.FetchAsync("not a url");

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FetchAsync_OversizedBody_IsAnError()
    {
        var (fetcher, _) = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[HttpFeedFetcher.MaxResponseBytes + 1]),
        }));

        var result = await fetcher.FetchAsync("https://feeds.example.test/huge.xml");

        Assert.True(result.IsError);
        Assert.Equal("Polling.FetchFailed", result.FirstError.Code);
    }

    [Fact]
    public void Configure_BoundsTimeAndSize()
    {
        using var client = new HttpClient();

        HttpFeedFetcher.Configure(client);

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Equal(HttpFeedFetcher.MaxResponseBytes, client.MaxResponseContentBufferSize);
    }

    [Fact]
    public void CreateHandler_EnablesAutomaticDecompression()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(HttpFeedFetcher.CreateHandler());

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }
}
