using ErrorOr;

namespace LuminaFeed.Services.Subscriptions;

/// <summary>
/// A user's feed subscriptions and their notification channels. The subscribe dialog (email + Slack switches),
/// Slack webhook validation and per-channel unsubscribe are the C1 flow; Slack <i>delivery</i> arrives with C2.
/// </summary>
/// <remarks>
/// <c>userId</c> must come from the authenticated principal, never from client input — these methods act on
/// whichever user they are given.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>The ids of the feeds the user is subscribed to (empty for an unknown or blank user).</summary>
    Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The channel state the subscribe dialog should open with for one (user, feed): the current channels when the
    /// user is already subscribed, or the defaults (email on, Slack off) when not, plus the Slack webhook to prefill.
    /// </summary>
    Task<ErrorOr<SubscriptionState>> GetSubscriptionForEditAsync(
        string? userId, Guid feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the user's subscription to the feed with the chosen channels (an upsert). The request is
    /// validated first (at least one channel; a genuine Slack webhook when Slack is on). Turning a single channel off
    /// is a per-channel unsubscribe; removing the subscription entirely is <see cref="UnsubscribeAsync"/>.
    /// </summary>
    Task<ErrorOr<Success>> SaveSubscriptionAsync(
        string? userId, Guid feedId, SaveSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the feed with email notifications. Idempotent: an existing subscription is kept and its
    /// email channel (re-)enabled. A convenience over <see cref="SaveSubscriptionAsync"/> for callers that only want email.
    /// </summary>
    Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default);

    /// <summary>Removes the user's subscription to the feed entirely (both channels).</summary>
    Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the subscribe dialog opens with for one (user, feed): whether a subscription already <paramref name="Exists"/>,
/// the current channel switches, and the webhook to prefill (the row's own, else the user's most recent one).
/// </summary>
public sealed record SubscriptionState(bool Exists, bool EmailEnabled, bool SlackEnabled, string? WebhookPrefill);
