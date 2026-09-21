using ErrorOr;

namespace LuminaFeed.Services.Subscriptions;

/// <summary>
/// A user's feed subscriptions. Walking-skeleton scope: <b>email only</b>. The channel dialog (email + Slack
/// switches), webhook validation and per-channel unsubscribe arrive with C1/C2/C5.
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
    /// Subscribes the user to the feed with email notifications. Idempotent: an existing subscription is kept
    /// and its email channel (re-)enabled.
    /// </summary>
    Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default);

    /// <summary>Removes the user's subscription to the feed entirely.</summary>
    Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default);
}
