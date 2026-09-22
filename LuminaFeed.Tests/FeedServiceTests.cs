using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;

namespace LuminaFeed.Tests;

/// <summary>Covers the feed catalogue service (S1 create/list) against real in-memory SQLite.</summary>
public sealed class FeedServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private FeedService CreateService() =>
        new(_db, new CreateFeedRequestValidator(), new UpdateFeedRequestValidator());

    private static CreateFeedRequest ValidRequest(Guid categoryId) => new(
        Name: "BBC News",
        CategoryId: categoryId,
        FeedUrl: "https://feeds.bbci.co.uk/news/rss.xml",
        SiteUrl: "https://www.bbc.com/news",
        ImageUrl: "https://www.bbc.com/logo.png",
        Description: "Headlines.",
        Popularity: 99472);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_PersistsFeed_AndReturnsSummaryWithCategoryName()
    {
        var category = _db.AddCategory("World News");

        var result = await CreateService().CreateAsync(ValidRequest(category.Id));

        Assert.False(result.IsError);
        Assert.Equal(7, result.Value.Id.Version);
        Assert.Equal("World News", result.Value.CategoryName);

        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Feeds);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("BBC News", stored.Name);
        Assert.Equal(category.Id, stored.CategoryId);
        Assert.Equal("https://feeds.bbci.co.uk/news/rss.xml", stored.FeedUrl);
        Assert.Equal("https://www.bbc.com/news", stored.SiteUrl);
        Assert.Equal("https://www.bbc.com/logo.png", stored.ImageUrl);
        Assert.Equal("Headlines.", stored.Description);
        Assert.Equal(99472, stored.Popularity);
        Assert.Null(stored.LastPolledAt);
    }

    [Fact]
    public async Task CreateAsync_TrimsInput_AndStoresBlankOptionalsAsNull()
    {
        var category = _db.AddCategory();
        var request = ValidRequest(category.Id) with
        {
            Name = "  BBC News ",
            FeedUrl = " https://feeds.bbci.co.uk/news/rss.xml ",
            ImageUrl = "   ",
            Description = "",
        };

        var result = await CreateService().CreateAsync(request);

        Assert.False(result.IsError);
        Assert.Equal("BBC News", result.Value.Name);
        Assert.Equal("https://feeds.bbci.co.uk/news/rss.xml", result.Value.FeedUrl);
        Assert.Null(result.Value.ImageUrl);
        Assert.Null(result.Value.Description);
    }

    [Fact]
    public async Task CreateAsync_EmptyRequest_ReportsEachRequiredField()
    {
        var result = await CreateService().CreateAsync(new CreateFeedRequest("", Guid.Empty, "", "", Popularity: -1));

        Assert.True(result.IsError);
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        var codes = result.Errors.Select(e => e.Code).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                nameof(CreateFeedRequest.Name),
                nameof(CreateFeedRequest.CategoryId),
                nameof(CreateFeedRequest.FeedUrl),
                nameof(CreateFeedRequest.SiteUrl),
                nameof(CreateFeedRequest.Popularity),
            },
            codes);

        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Feeds);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/rss.xml")]
    [InlineData("ftp://example.test/rss.xml")]
    [InlineData("javascript:alert(1)")]
    public async Task CreateAsync_NonHttpFeedUrl_IsRejected(string feedUrl)
    {
        var category = _db.AddCategory();

        var result = await CreateService().CreateAsync(ValidRequest(category.Id) with { FeedUrl = feedUrl });

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Type == ErrorType.Validation && e.Code == nameof(CreateFeedRequest.FeedUrl));
    }

    [Fact]
    public async Task CreateAsync_InvalidImageUrl_IsRejected_ButMissingImageIsFine()
    {
        var category = _db.AddCategory();
        var service = CreateService();

        var invalid = await service.CreateAsync(ValidRequest(category.Id) with { ImageUrl = "logo.png" });
        var missing = await service.CreateAsync(ValidRequest(category.Id) with { ImageUrl = null });

        Assert.True(invalid.IsError);
        Assert.Equal(nameof(CreateFeedRequest.ImageUrl), invalid.FirstError.Code);
        Assert.False(missing.IsError);
    }

    [Fact]
    public async Task CreateAsync_UnknownCategory_IsNotFound()
    {
        var result = await CreateService().CreateAsync(ValidRequest(Guid.CreateVersion7()));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Category.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task CreateAsync_DuplicateFeedUrl_IsAConflict_IgnoringCase()
    {
        var category = _db.AddCategory();
        var service = CreateService();
        Assert.False((await service.CreateAsync(ValidRequest(category.Id))).IsError);

        var result = await service.CreateAsync(ValidRequest(category.Id) with
        {
            Name = "BBC again",
            FeedUrl = "HTTPS://feeds.bbci.co.uk/news/rss.xml",
        });

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Feed.DuplicateFeedUrl", result.FirstError.Code);

        using var ctx = _db.CreateDbContext();
        Assert.Single(ctx.Feeds);
    }

    [Fact]
    public async Task ListAsync_OrdersByCategoryThenName()
    {
        var weather = _db.AddCategory("Weather");
        var markets = _db.AddCategory("Markets");
        _db.AddFeed(weather.Id, "NOAA");
        _db.AddFeed(weather.Id, "Met Office");
        _db.AddFeed(markets.Id, "Reuters Markets");

        var list = await CreateService().ListAsync();

        Assert.Equal(["Reuters Markets", "Met Office", "NOAA"], list.Select(f => f.Name));
        Assert.Equal(["Markets", "Weather", "Weather"], list.Select(f => f.CategoryName));
    }

    [Fact]
    public async Task ListByCategoryAsync_GroupsByCategoryName_WithFeedsByPopularityDescending()
    {
        var weather = _db.AddCategory("Weather");
        var markets = _db.AddCategory("Markets");
        _db.AddFeed(weather.Id, "NOAA", popularity: 10);
        _db.AddFeed(weather.Id, "Met Office", popularity: 500);
        _db.AddFeed(weather.Id, "AccuWeather", popularity: 10);
        _db.AddFeed(markets.Id, "Reuters Markets", popularity: 1);

        var groups = await CreateService().ListByCategoryAsync();

        Assert.Equal(["Markets", "Weather"], groups.Select(g => g.CategoryName));
        Assert.Equal(markets.Id, groups[0].CategoryId);
        Assert.Equal(["Reuters Markets"], groups[0].Feeds.Select(f => f.Name));
        // Popularity first (highest wins); name breaks the tie between the two 10s.
        Assert.Equal(["Met Office", "AccuWeather", "NOAA"], groups[1].Feeds.Select(f => f.Name));
        Assert.All(groups[1].Feeds, f => Assert.Equal("Weather", f.CategoryName));
    }

    [Fact]
    public async Task ListByCategoryAsync_OmitsCategoriesWithoutFeeds()
    {
        _db.AddCategory("Empty");
        var weather = _db.AddCategory("Weather");
        _db.AddFeed(weather.Id, "NOAA");

        var groups = await CreateService().ListByCategoryAsync();

        Assert.Equal("Weather", Assert.Single(groups).CategoryName);
    }

    [Fact]
    public async Task ListByCategoryAsync_EmptyCatalogue_ReturnsEmpty()
    {
        Assert.Empty(await CreateService().ListByCategoryAsync());
    }

    [Fact]
    public async Task ListByCategoryAsync_IncludesAFeedAddedThroughTheAdminService()
    {
        var category = _db.AddCategory("World News");
        var service = CreateService();

        var created = await service.CreateAsync(ValidRequest(category.Id));

        var feed = Assert.Single(Assert.Single(await service.ListByCategoryAsync()).Feeds);
        Assert.Equal(created.Value, feed);
    }

    // --- Update (A2) ------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_PersistsChanges_IncludingCategory_AndTrimsInput()
    {
        var world = _db.AddCategory("World News");
        var markets = _db.AddCategory("Markets");
        var feed = _db.AddFeed(world.Id, "BBC", "https://bbc.test/rss.xml", popularity: 1);

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "  BBC News ", markets.Id, " https://bbc.test/news.xml ", "https://bbc.test",
            ImageUrl: "https://bbc.test/logo.png", Description: "  Headlines. ", Popularity: 5));

        Assert.False(result.IsError);
        Assert.Equal("BBC News", result.Value.Name);
        Assert.Equal(markets.Id, result.Value.CategoryId);
        Assert.Equal("Markets", result.Value.CategoryName);
        Assert.Equal("https://bbc.test/news.xml", result.Value.FeedUrl);
        Assert.Equal("https://bbc.test/logo.png", result.Value.ImageUrl);
        Assert.Equal("Headlines.", result.Value.Description);
        Assert.Equal(5, result.Value.Popularity);

        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Feeds);
        Assert.Equal(markets.Id, stored.CategoryId);
        Assert.Equal("https://bbc.test/news.xml", stored.FeedUrl);
    }

    [Fact]
    public async Task UpdateAsync_BlankOptionals_AreStoredAsNull()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", category.Id, "https://bbc.test/rss.xml", "https://bbc.test", ImageUrl: "  ", Description: ""));

        Assert.False(result.IsError);
        Assert.Null(result.Value.ImageUrl);
        Assert.Null(result.Value.Description);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateFeedUrl_OfAnotherFeed_IsAConflict_IgnoringCase()
    {
        var category = _db.AddCategory();
        _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");
        var reuters = _db.AddFeed(category.Id, "Reuters", "https://reuters.test/rss.xml");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            reuters.Id, "Reuters", category.Id, "HTTPS://bbc.test/rss.xml", "https://reuters.test"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Feed.DuplicateFeedUrl", result.FirstError.Code);
    }

    [Fact]
    public async Task UpdateAsync_ChangingAFeedToACaseVariantOfItsOwnUrl_IsAllowed()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", category.Id, "HTTPS://BBC.test/rss.xml", "https://bbc.test"));

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task UpdateAsync_ChangingTheFeedUrl_ClearsTheCachedEtagAndLastModified()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");
        await SetPollingCache(feed.Id, etag: "\"abc123\"", lastModified: "Wed, 21 Oct 2026 07:28:00 GMT");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", category.Id, "https://bbc.test/news.xml", "https://bbc.test"));

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Feeds);
        Assert.Equal("https://bbc.test/news.xml", stored.FeedUrl);
        Assert.Null(stored.ETag);
        Assert.Null(stored.LastModified);
    }

    [Fact]
    public async Task UpdateAsync_LeavingTheFeedUrlUnchanged_KeepsTheCachedEtagAndLastModified()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");
        await SetPollingCache(feed.Id, etag: "\"abc123\"", lastModified: "Wed, 21 Oct 2026 07:28:00 GMT");

        // Same URL, only the popularity changes — the conditional-request cache must survive.
        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", category.Id, "https://bbc.test/rss.xml", "https://bbc.test", Popularity: 42));

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Feeds);
        Assert.Equal("\"abc123\"", stored.ETag);
        Assert.Equal("Wed, 21 Oct 2026 07:28:00 GMT", stored.LastModified);
    }

    private async Task SetPollingCache(Guid feedId, string etag, string lastModified)
    {
        await using var ctx = _db.CreateDbContext();
        var feed = await ctx.Feeds.FindAsync(feedId);
        feed!.ETag = etag;
        feed.LastModified = lastModified;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateAsync_UnknownFeed_IsNotFound()
    {
        var category = _db.AddCategory();

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            Guid.CreateVersion7(), "BBC", category.Id, "https://bbc.test/rss.xml", "https://bbc.test"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownCategory_IsNotFound()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", Guid.CreateVersion7(), "https://bbc.test/rss.xml", "https://bbc.test"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Category.NotFound", result.FirstError.Code);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    public async Task UpdateAsync_NonHttpFeedUrl_IsRejected(string feedUrl)
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");

        var result = await CreateService().UpdateAsync(new UpdateFeedRequest(
            feed.Id, "BBC", category.Id, feedUrl, "https://bbc.test"));

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Type == ErrorType.Validation && e.Code == nameof(UpdateFeedRequest.FeedUrl));
    }

    // --- Delete & deletion impact (A2) ------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesFeed_AndCascadesItsSubscriptionsAndArticles_LeavingOthers()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");
        var other = _db.AddFeed(category.Id, "Reuters", "https://reuters.test/rss.xml");
        var user = _db.AddUser();
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true });
            ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = other.Id, EmailEnabled = true });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a1", Title = "t1", Link = "https://bbc.test/1" });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a2", Title = "t2", Link = "https://bbc.test/2" });
            ctx.SaveChanges();
        }

        var result = await CreateService().DeleteAsync(feed.Id);

        Assert.False(result.IsError);
        using var check = _db.CreateDbContext();
        Assert.Equal(other.Id, Assert.Single(check.Feeds).Id);
        Assert.Empty(check.Articles);
        Assert.Equal(other.Id, Assert.Single(check.Subscriptions).FeedId);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNotFound()
    {
        var result = await CreateService().DeleteAsync(Guid.CreateVersion7());

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task GetDeletionImpactAsync_ReturnsSubscriptionAndArticleCounts()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC", "https://bbc.test/rss.xml");
        var user = _db.AddUser();
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a1", Title = "t1", Link = "https://bbc.test/1" });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a2", Title = "t2", Link = "https://bbc.test/2" });
            ctx.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a3", Title = "t3", Link = "https://bbc.test/3" });
            ctx.SaveChanges();
        }

        var result = await CreateService().GetDeletionImpactAsync(feed.Id);

        Assert.False(result.IsError);
        Assert.Equal("BBC", result.Value.Name);
        Assert.Equal(1, result.Value.SubscriptionCount);
        Assert.Equal(3, result.Value.ArticleCount);
    }

    [Fact]
    public async Task GetDeletionImpactAsync_UnknownId_IsNotFound()
    {
        var result = await CreateService().GetDeletionImpactAsync(Guid.CreateVersion7());

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    // --- B4: the feed page's feed + latest articles ----------------------------------------------

    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Article AddArticle(Guid feedId, string externalId, DateTimeOffset? publishedAt,
        string? summary = null, string? imageUrl = null)
    {
        using var ctx = _db.CreateDbContext();
        var article = new Article
        {
            FeedId = feedId,
            ExternalId = externalId,
            Title = externalId,
            Link = $"https://articles.test/{externalId}",
            Summary = summary,
            ImageUrl = imageUrl,
            PublishedAt = publishedAt,
        };
        ctx.Articles.Add(article);
        ctx.SaveChanges();
        return article;
    }

    [Fact]
    public async Task GetFeedDetailAsync_OrdersNewestFirst_WithDatelessLast_AndExcludesOtherFeeds()
    {
        var category = _db.AddCategory("World News");
        var feed = _db.AddFeed(category.Id, "BBC News");
        var other = _db.AddFeed(category.Id, "Sky News");
        AddArticle(feed.Id, "jan1", Jan1);
        AddArticle(feed.Id, "jan3", Jan1.AddDays(2));
        AddArticle(feed.Id, "jan2", Jan1.AddDays(1));
        AddArticle(feed.Id, "dateless", publishedAt: null);
        AddArticle(other.Id, "elsewhere", Jan1.AddDays(10));

        var detail = await CreateService().GetFeedDetailAsync(feed.Id, maxArticles: 10);

        Assert.NotNull(detail);
        Assert.Equal(feed.Id, detail!.Feed.Id);
        Assert.Equal("World News", detail.Feed.CategoryName);
        // Newest published first; the dateless article sorts last; the other feed's article is not included.
        Assert.Equal(["jan3", "jan2", "jan1", "dateless"], detail.Articles.Select(a => a.Title));
    }

    [Fact]
    public async Task GetFeedDetailAsync_HonoursTheCap_KeepingTheNewest()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC News");
        AddArticle(feed.Id, "jan1", Jan1);
        AddArticle(feed.Id, "jan3", Jan1.AddDays(2));
        AddArticle(feed.Id, "jan2", Jan1.AddDays(1));

        var detail = await CreateService().GetFeedDetailAsync(feed.Id, maxArticles: 2);

        Assert.NotNull(detail);
        Assert.Equal(["jan3", "jan2"], detail!.Articles.Select(a => a.Title));
    }

    [Fact]
    public async Task GetFeedDetailAsync_MapsArticleFields()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC News");
        AddArticle(feed.Id, "story", Jan1, summary: "First paragraph.", imageUrl: "https://img.test/a.png");

        var detail = await CreateService().GetFeedDetailAsync(feed.Id, maxArticles: 10);

        var article = Assert.Single(detail!.Articles);
        Assert.Equal("story", article.Title);
        Assert.Equal("https://articles.test/story", article.Link);
        Assert.Equal("First paragraph.", article.Summary);
        Assert.Equal("https://img.test/a.png", article.ImageUrl);
        Assert.Equal(Jan1, article.PublishedAt);
    }

    [Fact]
    public async Task GetFeedDetailAsync_FeedWithoutArticles_ReturnsTheFeedAndAnEmptyList()
    {
        var category = _db.AddCategory();
        var feed = _db.AddFeed(category.Id, "BBC News");

        var detail = await CreateService().GetFeedDetailAsync(feed.Id, maxArticles: 10);

        Assert.NotNull(detail);
        Assert.Equal(feed.Id, detail!.Feed.Id);
        Assert.Empty(detail.Articles);
    }

    [Fact]
    public async Task GetFeedDetailAsync_UnknownFeed_ReturnsNull()
    {
        Assert.Null(await CreateService().GetFeedDetailAsync(Guid.CreateVersion7(), maxArticles: 10));
    }
}
