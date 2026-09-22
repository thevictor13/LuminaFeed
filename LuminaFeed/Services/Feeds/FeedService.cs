using ErrorOr;
using FluentValidation;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Categories;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Feeds;

public sealed class FeedService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IValidator<CreateFeedRequest> validator,
    IValidator<UpdateFeedRequest> updateValidator) : IFeedService
{
    public async Task<IReadOnlyList<FeedSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Feeds
            .AsNoTracking()
            .OrderBy(f => f.Category.Name)
            .ThenBy(f => f.Name)
            .Select(f => new FeedSummary(
                f.Id, f.Name, f.CategoryId, f.Category.Name, f.FeedUrl, f.SiteUrl, f.ImageUrl, f.Description, f.Popularity))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryFeeds>> ListByCategoryAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var feeds = await db.Feeds
            .AsNoTracking()
            .OrderBy(f => f.Category.Name)
            .ThenByDescending(f => f.Popularity)
            .ThenBy(f => f.Name)
            .Select(f => new FeedSummary(
                f.Id, f.Name, f.CategoryId, f.Category.Name, f.FeedUrl, f.SiteUrl, f.ImageUrl, f.Description, f.Popularity))
            .ToListAsync(cancellationToken);

        // GroupBy keeps first-seen key order and element order, so the SQL ordering above carries through.
        return [.. feeds
            .GroupBy(f => (f.CategoryId, f.CategoryName))
            .Select(g => new CategoryFeeds(g.Key.CategoryId, g.Key.CategoryName, [.. g]))];
    }

    public async Task<ErrorOr<FeedSummary>> CreateAsync(
        CreateFeedRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            FeedUrl = request.FeedUrl?.Trim() ?? string.Empty,
            SiteUrl = request.SiteUrl?.Trim() ?? string.Empty,
            ImageUrl = NullIfBlank(request.ImageUrl),
            Description = NullIfBlank(request.Description),
        };

        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return validation.ToErrors();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var categoryName = await db.Categories
            .Where(c => c.Id == normalized.CategoryId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (categoryName is null)
            return CategoryErrors.NotFound(normalized.CategoryId);

        if (await FeedUrlExistsAsync(db, normalized.FeedUrl, cancellationToken))
            return FeedErrors.DuplicateFeedUrl(normalized.FeedUrl);

        var feed = new Feed
        {
            Name = normalized.Name,
            CategoryId = normalized.CategoryId,
            FeedUrl = normalized.FeedUrl,
            SiteUrl = normalized.SiteUrl,
            ImageUrl = normalized.ImageUrl,
            Description = normalized.Description,
            Popularity = normalized.Popularity,
        };
        db.Feeds.Add(feed);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent create on the unique FeedUrl index; anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FeedUrlExistsAsync(check, normalized.FeedUrl, cancellationToken))
                return FeedErrors.DuplicateFeedUrl(normalized.FeedUrl);
            throw;
        }

        return new FeedSummary(
            feed.Id, feed.Name, feed.CategoryId, categoryName, feed.FeedUrl, feed.SiteUrl, feed.ImageUrl,
            feed.Description, feed.Popularity);
    }

    public async Task<ErrorOr<FeedSummary>> UpdateAsync(
        UpdateFeedRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            FeedUrl = request.FeedUrl?.Trim() ?? string.Empty,
            SiteUrl = request.SiteUrl?.Trim() ?? string.Empty,
            ImageUrl = NullIfBlank(request.ImageUrl),
            Description = NullIfBlank(request.Description),
        };

        var validation = await updateValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return validation.ToErrors();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var feed = await db.Feeds.FirstOrDefaultAsync(f => f.Id == normalized.Id, cancellationToken);
        if (feed is null)
            return FeedErrors.NotFound(normalized.Id);

        var categoryName = await db.Categories
            .Where(c => c.Id == normalized.CategoryId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (categoryName is null)
            return CategoryErrors.NotFound(normalized.CategoryId);

        if (await FeedUrlExistsAsync(db, normalized.FeedUrl, cancellationToken, excludingId: normalized.Id))
            return FeedErrors.DuplicateFeedUrl(normalized.FeedUrl);

        // A URL change invalidates the cached conditional-request validators: the stored ETag / Last-Modified
        // describe the old endpoint, so a future conditional poll (C4) must not send them against the new URL
        // (that would risk a wrong 304 and skipped articles). Clear them so the next poll is a clean full GET.
        if (!string.Equals(feed.FeedUrl, normalized.FeedUrl, StringComparison.Ordinal))
        {
            feed.ETag = null;
            feed.LastModified = null;
        }

        feed.Name = normalized.Name;
        feed.CategoryId = normalized.CategoryId;
        feed.FeedUrl = normalized.FeedUrl;
        feed.SiteUrl = normalized.SiteUrl;
        feed.ImageUrl = normalized.ImageUrl;
        feed.Description = normalized.Description;
        feed.Popularity = normalized.Popularity;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent change onto the unique FeedUrl index; anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FeedUrlExistsAsync(check, normalized.FeedUrl, cancellationToken, excludingId: normalized.Id))
                return FeedErrors.DuplicateFeedUrl(normalized.FeedUrl);
            throw;
        }

        return new FeedSummary(
            feed.Id, feed.Name, feed.CategoryId, categoryName, feed.FeedUrl, feed.SiteUrl, feed.ImageUrl,
            feed.Description, feed.Popularity);
    }

    public async Task<ErrorOr<FeedDeletionImpact>> GetDeletionImpactAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var impact = await db.Feeds
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FeedDeletionImpact(f.Id, f.Name, f.Subscriptions.Count, f.Articles.Count))
            .SingleOrDefaultAsync(cancellationToken);
        if (impact is null)
            return FeedErrors.NotFound(id);

        return impact;
    }

    public async Task<ErrorOr<Deleted>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Subscription -> Feed is ClientCascade (the User owns the single DB cascade path into Subscriptions), so EF
        // must issue the feed's subscription deletes itself — they have to be loaded. Articles cascade at the DB.
        var feed = await db.Feeds
            .Include(f => f.Subscriptions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (feed is null)
            return FeedErrors.NotFound(id);

        db.Feeds.Remove(feed);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Deleted;
    }

    // The unique index on FeedUrl is case-sensitive (SQLite's default collation), so the friendlier case-insensitive
    // rule lives here. ToLower() on the column means this check scans rather than seeks — fine for a curated
    // catalogue of a few hundred rows, and it keeps the same semantics on a case-insensitive provider (SQL Server)
    // without a collation change. Editing passes its own id as excludingId so a feed doesn't collide with itself.
    private static Task<bool> FeedUrlExistsAsync(
        ApplicationDbContext db, string feedUrl, CancellationToken cancellationToken, Guid? excludingId = null)
    {
        var lowered = feedUrl.ToLowerInvariant();
        return db.Feeds.AnyAsync(
            f => f.FeedUrl.ToLower() == lowered && (excludingId == null || f.Id != excludingId), cancellationToken);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
