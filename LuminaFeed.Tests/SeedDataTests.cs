using LuminaFeed.Data;
using LuminaFeed.Data.Seed;
using LuminaFeed.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuminaFeed.Tests;

/// <summary>
/// Covers the G0.7 seeder: catalogue integrity, population, idempotency, and that re-seeding never
/// overwrites existing rows. Uses an in-memory SQLite database built from the EF model.
/// </summary>
public sealed class SeedDataTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SeedDataTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    private DatabaseSeeder CreateSeeder(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<DatabaseSeeder>.Instance);

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void EmbeddedCatalog_Loads_AndIsInternallyConsistent()
    {
        var catalog = SeedCatalogLoader.LoadEmbedded();

        Assert.Equal(10, catalog.Categories.Count);
        Assert.Equal(115, catalog.Feeds.Count);

        var categoryNames = catalog.Categories.Select(c => c.Name).ToHashSet();

        // Every feed references a known category.
        Assert.All(catalog.Feeds, f => Assert.Contains(f.Category, categoryNames));

        // Distinct popularity (deterministic default ordering) and distinct feed URLs.
        Assert.Equal(catalog.Feeds.Count, catalog.Feeds.Select(f => f.Popularity).Distinct().Count());
        Assert.Equal(catalog.Feeds.Count, catalog.Feeds.Select(f => f.FeedUrl).Distinct().Count());

        // Required string fields are populated.
        Assert.All(catalog.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.All(catalog.Feeds, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Name));
            Assert.False(string.IsNullOrWhiteSpace(f.FeedUrl));
            Assert.False(string.IsNullOrWhiteSpace(f.SiteUrl));
        });
    }

    [Fact]
    public async Task SeedAsync_PopulatesDatabase_WithResolvedCategories()
    {
        var catalog = SeedCatalogLoader.LoadEmbedded();

        using (var ctx = CreateContext())
        {
            await CreateSeeder(ctx).SeedAsync(catalog);
        }

        using (var ctx = CreateContext())
        {
            Assert.Equal(catalog.Categories.Count, await ctx.Categories.CountAsync());
            Assert.Equal(catalog.Feeds.Count, await ctx.Feeds.CountAsync());

            // Every feed resolved to a real category.
            Assert.False(await ctx.Feeds.AnyAsync(f => !ctx.Categories.Any(c => c.Id == f.CategoryId)));

            // Default ordering: the most popular feed is a top-tier publisher (BBC).
            var top = await ctx.Feeds.OrderByDescending(f => f.Popularity).FirstAsync();
            Assert.Contains("BBC", top.Name);
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        var catalog = SeedCatalogLoader.LoadEmbedded();

        using (var ctx = CreateContext()) await CreateSeeder(ctx).SeedAsync(catalog);
        using (var ctx = CreateContext()) await CreateSeeder(ctx).SeedAsync(catalog);

        using (var ctx = CreateContext())
        {
            Assert.Equal(catalog.Categories.Count, await ctx.Categories.CountAsync());
            Assert.Equal(catalog.Feeds.Count, await ctx.Feeds.CountAsync());
        }
    }

    [Fact]
    public async Task SeedAsync_DoesNotOverwrite_ExistingFeed()
    {
        var catalog = SeedCatalogLoader.LoadEmbedded();
        var sample = catalog.Feeds.First();

        // Pre-insert a category + a feed that shares the catalogue's FeedUrl but has edited values.
        using (var ctx = CreateContext())
        {
            var category = new Category { Name = sample.Category, Description = "edited" };
            ctx.Categories.Add(category);
            ctx.Feeds.Add(new Feed
            {
                Name = "ADMIN EDITED NAME",
                CategoryId = category.Id,
                FeedUrl = sample.FeedUrl,
                SiteUrl = sample.SiteUrl,
                Popularity = 1,
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext()) await CreateSeeder(ctx).SeedAsync(catalog);

        using (var ctx = CreateContext())
        {
            var feed = await ctx.Feeds.SingleAsync(f => f.FeedUrl == sample.FeedUrl);
            Assert.Equal("ADMIN EDITED NAME", feed.Name);
            Assert.Equal(1, feed.Popularity);

            // The rest of the catalogue still lands (existing category reused, not duplicated).
            Assert.Equal(catalog.Feeds.Count, await ctx.Feeds.CountAsync());
            Assert.Equal(catalog.Categories.Count, await ctx.Categories.CountAsync());
        }
    }

    [Fact]
    public async Task SeedAsync_AddsNewcomers_AfterPartialSeed()
    {
        var full = SeedCatalogLoader.LoadEmbedded();
        var subset = new SeedCatalog
        {
            Categories = [full.Categories[0]],
            Feeds = [full.Feeds.First(f => f.Category == full.Categories[0].Name)],
        };

        using (var ctx = CreateContext()) await CreateSeeder(ctx).SeedAsync(subset);

        using (var ctx = CreateContext())
        {
            Assert.Equal(1, await ctx.Feeds.CountAsync());
            await CreateSeeder(ctx).SeedAsync(full);
        }

        using (var ctx = CreateContext())
        {
            Assert.Equal(full.Categories.Count, await ctx.Categories.CountAsync());
            Assert.Equal(full.Feeds.Count, await ctx.Feeds.CountAsync());
        }
    }
}
