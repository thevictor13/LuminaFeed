using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;

namespace LuminaFeed.Tests;

/// <summary>Covers the feed catalogue service (S1 create/list) against real in-memory SQLite.</summary>
public sealed class FeedServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private FeedService CreateService() => new(_db, new CreateFeedRequestValidator());

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

    [Fact]
    public void ValidatorLimits_MatchTheColumnLimits()
    {
        using var ctx = _db.CreateDbContext();
        var entity = ctx.Model.FindEntityType(typeof(Feed))!;

        Assert.Equal(CreateFeedRequestValidator.NameMaxLength, entity.FindProperty(nameof(Feed.Name))!.GetMaxLength());
        Assert.Equal(CreateFeedRequestValidator.UrlMaxLength, entity.FindProperty(nameof(Feed.FeedUrl))!.GetMaxLength());
        Assert.Equal(CreateFeedRequestValidator.UrlMaxLength, entity.FindProperty(nameof(Feed.SiteUrl))!.GetMaxLength());
        Assert.Equal(CreateFeedRequestValidator.UrlMaxLength, entity.FindProperty(nameof(Feed.ImageUrl))!.GetMaxLength());
        Assert.Equal(CreateFeedRequestValidator.DescriptionMaxLength, entity.FindProperty(nameof(Feed.Description))!.GetMaxLength());
    }
}
