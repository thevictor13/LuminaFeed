using ErrorOr;
using LuminaFeed.Data;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Users;

public sealed class UserAdminService(IDbContextFactory<ApplicationDbContext> dbFactory) : IUserAdminService
{
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserSummary(
                u.Id,
                u.Email ?? u.UserName ?? u.Id,
                u.IsAdmin,
                u.Subscriptions
                    .OrderBy(s => s.Feed.Name)
                    .Select(s => new UserSubscriptionSummary(s.FeedId, s.Feed.Name, s.EmailEnabled, s.SlackEnabled))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<ErrorOr<Deleted>> RemoveSubscriptionAsync(
        string userId, Guid feedId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Subscriptions
            .Where(s => s.UserId == userId && s.FeedId == feedId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? UserErrors.SubscriptionNotFound : Result.Deleted;
    }

    public async Task<ErrorOr<int>> GetSubscriptionCountAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var found = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { Count = u.Subscriptions.Count })
            .SingleOrDefaultAsync(cancellationToken);

        return found is null ? UserErrors.UserNotFound : found.Count;
    }

    public async Task<ErrorOr<Deleted>> DeleteUserAsync(
        string userId, string actingAdminId, CancellationToken cancellationToken = default)
    {
        // Guard the acting admin from locking themselves out (also enforced by the disabled button on their own row).
        // An empty acting id is a programming error (the id must come from the authenticated principal) and would
        // otherwise slip past the self-check, so it is refused too.
        if (string.IsNullOrEmpty(actingAdminId) || userId == actingAdminId)
            return UserErrors.CannotDeleteSelf;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return UserErrors.UserNotFound;

        // Subscription → User is DB Cascade (the single DB cascade path chosen in G0.3/G0.8), so removing the user
        // deletes their subscriptions at the database; Identity's own satellite rows cascade the same way. Unlike a
        // feed delete (Subscription → Feed is ClientCascade) there is nothing to Include.
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Deleted;
    }
}
