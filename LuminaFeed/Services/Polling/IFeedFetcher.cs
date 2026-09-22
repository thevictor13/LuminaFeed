using ErrorOr;

namespace LuminaFeed.Services.Polling;

/// <summary>Downloads a feed document. Failures are values, not exceptions, so one bad feed can't stop a poll.</summary>
public interface IFeedFetcher
{
    /// <summary>The raw response body (the XML declaration decides its encoding), or why it couldn't be fetched.</summary>
    Task<ErrorOr<byte[]>> FetchAsync(string feedUrl, CancellationToken cancellationToken = default);
}
