using LuminaFeed.Services.Notifications;

namespace LuminaFeed.Services.Polling;

/// <summary>One polling pass over the active feeds.</summary>
public interface IFeedPollingService
{
    /// <summary>
    /// Polls every feed that has at least one subscriber, stores the articles not seen before, and returns the
    /// articles to notify about, keyed by <b>subscriber email address</b> (email-enabled subscriptions only).
    /// </summary>
    /// <remarks>
    /// Keyed by email as the spec asks. The value is deliberately a plain <see cref="NewArticle"/> list; with
    /// per-subscriber channel dispatch (C4) it grows into a per-subscription digest (channels, Slack webhook,
    /// subscription id) while the key stays the same.
    /// </remarks>
    Task<IReadOnlyDictionary<string, IReadOnlyList<NewArticle>>> PollAsync(CancellationToken cancellationToken = default);
}
