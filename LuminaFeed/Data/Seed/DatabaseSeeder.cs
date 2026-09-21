using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuminaFeed.Data.Seed;

/// <summary>
/// Loads the G0.R research catalogue into the database (G0.7). Upserts by natural key
/// (<see cref="Category.Name"/>, <see cref="Feed.FeedUrl"/>) and is <b>insert-missing-only</b>: existing rows are
/// never modified, so the seeder is idempotent and does not clobber later admin edits (A1/A2 CRUD).
/// </summary>
internal sealed class DatabaseSeeder(ApplicationDbContext db, ILogger<DatabaseSeeder> logger)
{
    /// <summary>Seeds from the embedded research catalogue.</summary>
    public Task SeedAsync(CancellationToken cancellationToken = default) =>
        SeedAsync(SeedCatalogLoader.LoadEmbedded(), cancellationToken);

    /// <summary>Seeds from the supplied catalogue (used directly by tests).</summary>
    public async Task SeedAsync(SeedCatalog catalog, CancellationToken cancellationToken = default)
    {
        var categoriesAdded = await SeedCategoriesAsync(catalog, cancellationToken);
        var (feedsAdded, feedsSkipped) = await SeedFeedsAsync(catalog, cancellationToken);

        logger.LogInformation(
            "Seed complete: {CategoriesAdded} categories added, {FeedsAdded} feeds added, {FeedsSkipped} feeds already present.",
            categoriesAdded, feedsAdded, feedsSkipped);
    }

    private async Task<int> SeedCategoriesAsync(SeedCatalog catalog, CancellationToken cancellationToken)
    {
        var existing = await db.Categories
            .Select(c => c.Name)
            .ToHashSetAsync(cancellationToken);

        var added = 0;
        foreach (var seed in catalog.Categories)
        {
            if (existing.Contains(seed.Name))
                continue;

            db.Categories.Add(new Category { Name = seed.Name, Description = seed.Description });
            existing.Add(seed.Name);
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        return added;
    }

    private async Task<(int Added, int Skipped)> SeedFeedsAsync(SeedCatalog catalog, CancellationToken cancellationToken)
    {
        var categoryIdByName = await db.Categories
            .ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);

        var existingUrls = await db.Feeds
            .Select(f => f.FeedUrl)
            .ToHashSetAsync(cancellationToken);

        var added = 0;
        var skipped = 0;
        foreach (var seed in catalog.Feeds)
        {
            if (existingUrls.Contains(seed.FeedUrl))
            {
                skipped++;
                continue;
            }

            if (!categoryIdByName.TryGetValue(seed.Category, out var categoryId))
            {
                logger.LogWarning(
                    "Skipping feed '{Feed}': its category '{Category}' is not in the catalogue.",
                    seed.Name, seed.Category);
                continue;
            }

            db.Feeds.Add(new Feed
            {
                Name = seed.Name,
                CategoryId = categoryId,
                FeedUrl = seed.FeedUrl,
                SiteUrl = seed.SiteUrl,
                ImageUrl = seed.ImageUrl,
                Description = seed.Description,
                Popularity = seed.Popularity,
            });
            existingUrls.Add(seed.FeedUrl);
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        return (added, skipped);
    }
}
