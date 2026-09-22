using ErrorOr;

namespace LuminaFeed.Services.Users;

/// <summary>
/// Admin-only user management (A3): list the signed-up users and their subscribed feeds, remove an individual
/// subscription, and delete a user registration entirely.
/// </summary>
/// <remarks>
/// The acting admin's id (<c>actingAdminId</c>) must come from the authenticated principal, never from client input —
/// it is what guards an admin from deleting their own account.
/// </remarks>
public interface IUserAdminService
{
    /// <summary>Every user, ordered by email, each with the feeds they are subscribed to.</summary>
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes one user's subscription to one feed (the whole row: both channels).</summary>
    Task<ErrorOr<Deleted>> RemoveSubscriptionAsync(string userId, Guid feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many subscriptions a delete would remove for this user, read fresh for the confirmation dialog (so its
    /// warning doesn't rely on the possibly-stale list snapshot); not-found for an unknown id.
    /// </summary>
    Task<ErrorOr<int>> GetSubscriptionCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user registration; the user's subscriptions cascade at the database. Refuses to delete the acting
    /// admin's own account (<see cref="UserErrors.CannotDeleteSelf"/>).
    /// </summary>
    Task<ErrorOr<Deleted>> DeleteUserAsync(string userId, string actingAdminId, CancellationToken cancellationToken = default);
}

/// <summary>A signed-up user and the feeds they subscribe to, for the admin list.</summary>
public sealed record UserSummary(string Id, string Email, bool IsAdmin, IReadOnlyList<UserSubscriptionSummary> Subscriptions);

/// <summary>One of a user's feed subscriptions, with the channels it delivers on.</summary>
public sealed record UserSubscriptionSummary(Guid FeedId, string FeedName, bool EmailEnabled, bool SlackEnabled);
