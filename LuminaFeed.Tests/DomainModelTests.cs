using LuminaFeed.Data;
using LuminaFeed.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// Exercises the domain model against a real (in-memory) SQLite database built from the EF model,
/// covering ids, relationships, unique indexes and delete behaviour.
/// </summary>
public sealed class DomainModelTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DomainModelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    /// <summary>Returns a unique key each call so the model is always rebuilt (never served from the
    /// process-wide model cache) — used by the drift test to build the app-accurate Version3 model.</summary>
    private sealed class UniqueModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => Guid.NewGuid();
    }

    public void Dispose() => _connection.Dispose();

    private static ApplicationUser NewUser() =>
        new() { UserName = "alice", Email = "alice@example.test" };

    private static Category NewCategory() => new() { Name = "World News", Description = "Global." };

    private static Feed NewFeed(Guid categoryId) => new()
    {
        Name = "BBC News",
        CategoryId = categoryId,
        FeedUrl = "https://feeds.bbci.co.uk/news/rss.xml",
        SiteUrl = "https://www.bbc.com/news",
        Popularity = 99472,
    };

    [Fact]
    public void EntityBase_Id_IsNonEmptyVersion7Guid()
    {
        var category = NewCategory();
        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal(7, category.Id.Version);
    }

    [Fact]
    public void FullGraph_RoundTrips()
    {
        var user = NewUser();
        var category = NewCategory();
        var feed = NewFeed(category.Id);
        var subscription = new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true };
        var article = new Article
        {
            FeedId = feed.Id,
            ExternalId = "guid-1",
            Title = "Headline",
            Link = "https://www.bbc.com/news/article-1",
        };

        using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            ctx.Categories.Add(category);
            ctx.Feeds.Add(feed);
            ctx.Subscriptions.Add(subscription);
            ctx.Articles.Add(article);
            ctx.SaveChanges();
        }

        using (var ctx = CreateContext())
        {
            var reloaded = ctx.Feeds
                .Include(f => f.Category)
                .Include(f => f.Subscriptions)
                .Include(f => f.Articles)
                .Single();

            Assert.Equal("World News", reloaded.Category.Name);
            Assert.Single(reloaded.Subscriptions);
            Assert.True(reloaded.Subscriptions.First().EmailEnabled);
            Assert.Single(reloaded.Articles);
            Assert.Equal("Headline", reloaded.Articles.First().Title);
        }
    }

    [Fact]
    public void Subscription_UserFeed_IsUnique()
    {
        var user = NewUser();
        var category = NewCategory();
        var feed = NewFeed(category.Id);

        using var ctx = CreateContext();
        ctx.Users.Add(user);
        ctx.Categories.Add(category);
        ctx.Feeds.Add(feed);
        ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true });
        ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, SlackEnabled = true });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void Article_FeedExternalId_IsUnique()
    {
        var category = NewCategory();
        var feed = NewFeed(category.Id);

        using var ctx = CreateContext();
        ctx.Categories.Add(category);
        ctx.Feeds.Add(feed);
        ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "dup", Title = "A", Link = "https://x.test/a" });
        ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "dup", Title = "B", Link = "https://x.test/b" });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void DeletingFeed_CascadesToArticlesAndSubscriptions()
    {
        var user = NewUser();
        var category = NewCategory();
        var feed = NewFeed(category.Id);

        using (var ctx = CreateContext())
        {
            ctx.Users.Add(user);
            ctx.Categories.Add(category);
            ctx.Feeds.Add(feed);
            ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "g1", Title = "T", Link = "https://x.test/t" });
            ctx.SaveChanges();
        }

        using (var ctx = CreateContext())
        {
            // Subscriptions use ClientCascade (see FeedConfiguration), so they must be tracked for EF
            // to cascade the delete; the app deletes feeds by loading them with their dependents.
            var feedToDelete = ctx.Feeds
                .Include(f => f.Subscriptions)
                .Include(f => f.Articles)
                .Single();
            ctx.Feeds.Remove(feedToDelete);
            ctx.SaveChanges();
        }

        using (var ctx = CreateContext())
        {
            Assert.Empty(ctx.Articles);
            Assert.Empty(ctx.Subscriptions);
        }
    }

    [Fact]
    public void Model_MatchesMigrationSnapshot_NoDrift()
    {
        // Guards against schema drift: the EF model must match the migrations' snapshot, i.e.
        // `dotnet ef migrations add` would produce an empty migration. HostBootTests legitimately
        // suppresses PendingModelChangesWarning (a WebApplicationFactory false positive), so this is
        // the real drift check — it compares the snapshot's relational model to the current one, the
        // same diff the CLI performs.
        //
        // The model must be built exactly as the app builds it: Program.cs sets
        // IdentityOptions.Stores.SchemaVersion = Version3 (which adds the passkeys table), so a bare
        // `new ApplicationDbContext(...)` would build a *default*-schema model and report a spurious
        // difference. We therefore resolve the context from an Identity-configured provider.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o
            .UseSqlite(_connection)
            // EF caches the model per context type across the process, and IdentityOptions.SchemaVersion
            // is not part of that key — so a sibling test that builds a default-schema ApplicationDbContext
            // first would poison the cache. A unique cache key forces a fresh Version3 build here.
            .ReplaceService<IModelCacheKeyFactory, UniqueModelCacheKeyFactory>());
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var snapshot = ctx.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var snapshotModel = snapshot!.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }
        snapshotModel = ctx.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);

        var currentModel = ctx.GetService<IDesignTimeModel>().Model;

#pragma warning disable EF1001 // IMigrationsModelDiffer is an internal EF API, used here only as a test-time drift check.
        var differ = ctx.GetService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());
#pragma warning restore EF1001

        var summary = string.Join("; ", ops.Select(o => o.GetType().Name));
        Assert.False(
            ops.Count > 0,
            $"The EF model has changes not captured by a migration ({ops.Count}): {summary}");
    }

    [Fact]
    public void Feed_HasIndexOn_CategoryIdAndPopularity()
    {
        using var ctx = CreateContext();
        var feedType = ctx.Model.FindEntityType(typeof(Feed))!;

        var hasCompositeIndex = feedType.GetIndexes().Any(i =>
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(Feed.CategoryId), nameof(Feed.Popularity)]));

        Assert.True(hasCompositeIndex, "Expected a (CategoryId, Popularity) index to back the default public sort.");
    }

    [Fact]
    public void DeletingCategory_WithFeeds_IsRestricted()
    {
        var category = NewCategory();
        var feed = NewFeed(category.Id);

        using (var ctx = CreateContext())
        {
            ctx.Categories.Add(category);
            ctx.Feeds.Add(feed);
            ctx.SaveChanges();
        }

        using (var ctx = CreateContext())
        {
            ctx.Categories.Remove(ctx.Categories.Single());
            Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
        }
    }
}
