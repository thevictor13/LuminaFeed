using ErrorOr;
using FluentValidation;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Subscriptions;

public sealed class SubscriptionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IValidator<SaveSubscriptionRequest> validator) : ISubscriptionService
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

    public async Task<ErrorOr<SubscriptionState>> GetSubscriptionForEditAsync(
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

        // Prefill the webhook from this subscription's own value, else the user's most recently created webhook
        // anywhere (the spec's LastOrDefault) — so re-enabling Slack doesn't make them paste it again. A user has
        // few subscriptions, and SQLite can't ORDER BY a DateTimeOffset, so the newest is picked client-side.
        var prefill = existing?.SlackWebhookUrl;
        if (string.IsNullOrEmpty(prefill))
        {
            var candidates = await db.Subscriptions
                .Where(s => s.UserId == userId && s.SlackWebhookUrl != null && s.SlackWebhookUrl != "")
                .Select(s => new { s.SlackWebhookUrl, s.CreatedAt })
                .ToListAsync(cancellationToken);
            prefill = candidates
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => c.SlackWebhookUrl)
                .FirstOrDefault();
        }

        return new SubscriptionState(
            Exists: existing is not null,
            EmailEnabled: existing?.EmailEnabled ?? true,
            SlackEnabled: existing?.SlackEnabled ?? false,
            WebhookPrefill: prefill);
    }

    public async Task<ErrorOr<Success>> SaveSubscriptionAsync(
        string? userId, Guid feedId, SaveSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return SubscriptionErrors.UserRequired;

        var normalized = request with
        {
            SlackWebhookUrl = string.IsNullOrWhiteSpace(request.SlackWebhookUrl) ? null : request.SlackWebhookUrl.Trim(),
        };

        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return validation.ToErrors();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.Feeds.AnyAsync(f => f.Id == feedId, cancellationToken))
            return FeedErrors.NotFound(feedId);
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return SubscriptionErrors.UserNotFound;

        var existing = await FindAsync(db, userId, feedId, cancellationToken);
        if (existing is not null)
        {
            Apply(existing, normalized);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }

        db.Subscriptions.Add(new Subscription
        {
            UserId = userId,
            FeedId = feedId,
            EmailEnabled = normalized.EmailEnabled,
            SlackEnabled = normalized.SlackEnabled,
            SlackWebhookUrl = normalized.SlackWebhookUrl,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent subscribe (e.g. a double click) won the unique (UserId, FeedId) index. Apply the chosen
            // channels to the winning row so the user's intent still takes effect; a missing row is a real fault.
            await using var retry = await dbFactory.CreateDbContextAsync(cancellationToken);
            var winner = await FindAsync(retry, userId, feedId, cancellationToken);
            if (winner is null)
                throw;

            Apply(winner, normalized);
            await retry.SaveChangesAsync(cancellationToken);
        }

        return Result.Success;
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

    /// <summary>
    /// Applies the chosen channels to a subscription. A supplied webhook is stored; when none is supplied the
    /// existing one is kept (the invariant is one-directional — Slack off never requires the webhook be cleared —
    /// so keeping it preserves the LastOrDefault prefill).
    /// </summary>
    private static void Apply(Subscription subscription, SaveSubscriptionRequest request)
    {
        subscription.EmailEnabled = request.EmailEnabled;
        subscription.SlackEnabled = request.SlackEnabled;
        if (request.SlackWebhookUrl is not null)
            subscription.SlackWebhookUrl = request.SlackWebhookUrl;
    }

    private static Task<Subscription?> FindAsync(
        ApplicationDbContext db, string userId, Guid feedId, CancellationToken cancellationToken) =>
        db.Subscriptions.SingleOrDefaultAsync(s => s.UserId == userId && s.FeedId == feedId, cancellationToken);
}
