using System.Text;
using ErrorOr;
using LuminaFeed.Services.Polling;

namespace LuminaFeed.Tests;

/// <summary>An <see cref="IFeedFetcher"/> serving canned documents per URL and recording what was asked for.</summary>
internal sealed class FakeFeedFetcher : IFeedFetcher
{
    private readonly Dictionary<string, Func<ErrorOr<byte[]>>> _responses = new(StringComparer.Ordinal);

    public List<string> RequestedUrls { get; } = [];

    public void Serve(string feedUrl, string xml) => _responses[feedUrl] = () => Encoding.UTF8.GetBytes(xml);

    public void Fail(string feedUrl) => _responses[feedUrl] = () => PollingErrors.HttpStatus(feedUrl, 503);

    public void Throw(string feedUrl, Exception exception) => _responses[feedUrl] = () => throw exception;

    public Task<ErrorOr<byte[]>> FetchAsync(string feedUrl, CancellationToken cancellationToken = default)
    {
        RequestedUrls.Add(feedUrl);
        return Task.FromResult(_responses.TryGetValue(feedUrl, out var respond)
            ? respond()
            : PollingErrors.HttpStatus(feedUrl, 404));
    }
}

/// <summary>Keeps host-booting tests off the network: the real catalogue's URLs must never be fetched by a test.</summary>
internal sealed class NoNetworkFeedFetcher : IFeedFetcher
{
    public Task<ErrorOr<byte[]>> FetchAsync(string feedUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<ErrorOr<byte[]>>(PollingErrors.FetchFailed(feedUrl, "network access is disabled in tests."));
}

/// <summary>Builds small RSS documents for polling tests.</summary>
internal static class RssDocument
{
    public static string With(params (string Guid, string Title, string? PubDate)[] items)
    {
        var xml = new StringBuilder("<rss version=\"2.0\"><channel><title>Test feed</title>");
        foreach (var (guid, title, pubDate) in items)
        {
            xml.Append("<item>")
                .Append($"<guid isPermaLink=\"false\">{guid}</guid>")
                .Append($"<title>{title}</title>")
                .Append($"<link>https://articles.example.test/{guid}</link>")
                .Append(pubDate is null ? string.Empty : $"<pubDate>{pubDate}</pubDate>")
                .Append("</item>");
        }

        return xml.Append("</channel></rss>").ToString();
    }

    /// <summary><paramref name="count"/> items, <c>item-1</c> the oldest … <c>item-N</c> the newest, newest first in the document.</summary>
    public static string WithSequence(int count, int startAt = 1) => With([.. Enumerable
        .Range(startAt, count)
        .Reverse()
        .Select(n => ($"item-{n}", $"Article {n}", (string?)new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n).ToString("r")))]);
}
