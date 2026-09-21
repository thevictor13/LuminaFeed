using LuminaFeed.Data;
using LuminaFeed.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
            ctx.Feeds.Remove(ctx.Feeds.Single());
            ctx.SaveChanges();
        }

        using (var ctx = CreateContext())
        {
            Assert.Empty(ctx.Articles);
            Assert.Empty(ctx.Subscriptions);
        }
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
