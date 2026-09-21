using ErrorOr;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Subscriptions;

public sealed class SubscriptionService(IDbContextFactory<ApplicationDbContext> dbFactory) : ISubscriptionService
{
    public async Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(
        string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new HashSet<Guid>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Subscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.FeedId)
            .ToHashSetAsync(cancellationToken);
    }

    public async Task<ErrorOr<Success>> SubscribeByEmailAsync(
        string? userId, Guid feedId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return SubscriptionErrors.UserRequired;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.Feeds.AnyAsync(f => f.Id == feedId, cancellationToken))
            return FeedErrors.NotFound(feedId);
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return SubscriptionErrors.UserNotFound;

        var existing = await FindAsync(db, userId, feedId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.EmailEnabled)
            {
                existing.EmailEnabled = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result.Success;
        }

        db.Subscriptions.Add(new Subscription { UserId = userId, FeedId = feedId, EmailEnabled = true });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent subscribe (e.g. a double click) won the unique (UserId, FeedId) index: the desired
            // end state already holds, so that is a success. Anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindAsync(check, userId, feedId, cancellationToken) is null)
                throw;
        }

        return Result.Success;
    }

    public async Task<ErrorOr<Deleted>> UnsubscribeAsync(
        string? userId, Guid feedId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return SubscriptionErrors.UserRequired;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Subscriptions
            .Where(s => s.UserId == userId && s.FeedId == feedId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? SubscriptionErrors.NotSubscribed : Result.Deleted;
    }

    private static Task<Subscription?> FindAsync(
        ApplicationDbContext db, string userId, Guid feedId, CancellationToken cancellationToken) =>
        db.Subscriptions.SingleOrDefaultAsync(s => s.UserId == userId && s.FeedId == feedId, cancellationToken);
}
