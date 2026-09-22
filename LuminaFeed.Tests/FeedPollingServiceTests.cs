using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Options;
using LuminaFeed.Services.Polling;
using Microsoft.Extensions.Logging;

namespace LuminaFeed.Tests;

/// <summary>Covers one polling pass (S4) against real in-memory SQLite with a canned fetcher.</summary>
public sealed class FeedPollingServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly SqliteTestDatabase _db = new();
    private readonly FakeFeedFetcher _fetcher = new();
    private readonly ListLogger<FeedPollingService> _logger = new();

    private FeedPollingService CreateService(int firstPollCap = 5) => new(
        _db,
        _fetcher,
        Microsoft.Extensions.Options.Options.Create(new PollingOptions { FirstPollNotificationCap = firstPollCap }),
        new FixedTimeProvider(Now),
        _logger);

    public void Dispose() => _db.Dispose();

    private Feed AddFeed(string name) => _db.AddFeed(_db.AddCategory(name + " category").Id, name, $"https://feeds.example.test/{name}.xml");

    private void Subscribe(ApplicationUser user, Feed feed, bool emailEnabled = true)
    {
        using var ctx = _db.CreateDbContext();
        ctx.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = emailEnabled });
        ctx.SaveChanges();
    }

    private List<Article> StoredArticles()
    {
        using var ctx = _db.CreateDbContext();
        return [.. ctx.Articles];
    }

    [Fact]
    public async Task PollAsync_OnlyFetchesFeedsWithAtLeastOneSubscriber()
    {
        var subscribed = AddFeed("subscribed");
        AddFeed("ignored");
        Subscribe(_db.AddUser(), subscribed);
        _fetcher.Serve(subscribed.FeedUrl, RssDocument.WithSequence(1));

        await CreateService().PollAsync();

        Assert.Equal([subscribed.FeedUrl], _fetcher.RequestedUrls);
    }

    [Fact]
    public async Task PollAsync_NoSubscriptions_DoesNothing()
    {
        AddFeed("lonely");

        var result = await CreateService().PollAsync();

        Assert.Empty(result);
        Assert.Empty(_fetcher.RequestedUrls);
    }

    [Fact]
    public async Task PollAsync_PersistsFetchedItemsAsArticles_AndStampsLastPolledAt()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.With(("guid-1", "Headline", "Mon, 21 Sep 2026 10:30:00 GMT")));

        await CreateService().PollAsync();

        var article = Assert.Single(StoredArticles());
        Assert.Equal(feed.Id, article.FeedId);
        Assert.Equal("guid-1", article.ExternalId);
        Assert.Equal("Headline", article.Title);
        Assert.Equal("https://articles.example.test/guid-1", article.Link);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 30, 0, TimeSpan.Zero), article.PublishedAt);
        Assert.Equal(7, article.Id.Version);

        using var ctx = _db.CreateDbContext();
        Assert.Equal(Now, ctx.Feeds.Single(f => f.Id == feed.Id).LastPolledAt);
    }

    [Fact]
    public async Task PollAsync_ReturnsNewArticlesKeyedBySubscriberEmail_AsValuesNamingTheirFeed()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser("alice@example.test"), feed);
        Subscribe(_db.AddUser("bob@example.test"), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(2));

        var result = await CreateService().PollAsync();

        Assert.Equal(["alice@example.test", "bob@example.test"], result.Keys.Order());
        var storedIds = StoredArticles().Select(a => a.Id).ToHashSet();
        Assert.All(result.Values, articles =>
        {
            Assert.Equal(["Article 2", "Article 1"], articles.Select(a => a.Title));
            Assert.All(articles, a =>
            {
                // A plain value: the feed's id and name travel with it, and it points at the stored row.
                Assert.Equal(feed.Id, a.FeedId);
                Assert.Equal("bbc", a.FeedName);
                Assert.Contains(a.ArticleId, storedIds);
            });
        });
        Assert.True(result.ContainsKey("ALICE@example.test"), "Email keys are case-insensitive.");
    }

    [Fact]
    public async Task PollAsync_SecondPoll_StoresAndReportsOnlyUnseenItems()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        var service = CreateService();
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(3));
        await service.PollAsync();

        // Items 1-3 are already known; 4 and 5 are new.
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(5));
        var second = await service.PollAsync();

        Assert.Equal(["Article 5", "Article 4"], Assert.Single(second).Value.Select(a => a.Title));
        Assert.Equal(5, StoredArticles().Count);
    }

    [Fact]
    public async Task PollAsync_UnchangedFeed_ReportsNothing()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        var service = CreateService();
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(3));
        await service.PollAsync();

        var second = await service.PollAsync();

        Assert.Empty(second);
        Assert.Equal(3, StoredArticles().Count);
    }

    [Fact]
    public async Task PollAsync_FirstPoll_StoresEverything_ButReportsOnlyTheNewestFive()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(12));

        var result = await CreateService().PollAsync();

        Assert.Equal(12, StoredArticles().Count);
        Assert.Equal(
            ["Article 12", "Article 11", "Article 10", "Article 9", "Article 8"],
            Assert.Single(result).Value.Select(a => a.Title));
    }

    [Fact]
    public async Task PollAsync_FirstPoll_PicksTheNewestByDate_NotByDocumentOrder()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.With(
            ("old", "Old", "Mon, 01 Sep 2025 00:00:00 GMT"),
            ("undated", "Undated", null),
            ("newest", "Newest", "Mon, 21 Sep 2026 00:00:00 GMT"),
            ("newer", "Newer", "Sun, 20 Sep 2026 00:00:00 GMT")));

        var result = await CreateService(firstPollCap: 3).PollAsync();

        // Dated items newest-first; the undated one can't be ranked, so it goes last and misses the cap.
        Assert.Equal(["Newest", "Newer", "Old"], Assert.Single(result).Value.Select(a => a.Title));
    }

    [Fact]
    public async Task PollAsync_LaterPolls_AreNotCapped()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        var service = CreateService(firstPollCap: 2);
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(3));
        await service.PollAsync();

        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(13));
        var second = await service.PollAsync();

        Assert.Equal(10, Assert.Single(second).Value.Count);
    }

    [Fact]
    public async Task PollAsync_CapOfZero_MakesTheFirstPollASilentBaseline()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(4));

        var result = await CreateService(firstPollCap: 0).PollAsync();

        Assert.Empty(result);
        Assert.Equal(4, StoredArticles().Count);
    }

    [Fact]
    public async Task PollAsync_FailedFirstFetch_KeepsTheFeedOnFirstPollStatus()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        var service = CreateService(firstPollCap: 2);
        _fetcher.Fail(feed.FeedUrl);
        await service.PollAsync();

        using (var ctx = _db.CreateDbContext())
        {
            Assert.Null(ctx.Feeds.Single(f => f.Id == feed.Id).LastPolledAt);
        }

        // The first *successful* poll is still the capped one.
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(6));
        var result = await service.PollAsync();

        Assert.Equal(2, Assert.Single(result).Value.Count);
        Assert.Equal(6, StoredArticles().Count);
    }

    [Fact]
    public async Task PollAsync_OnlyEmailEnabledConfirmedSubscribersAreReported()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser("email-on@example.test"), feed);
        Subscribe(_db.AddUser("email-off@example.test"), feed, emailEnabled: false);
        var unconfirmed = _db.AddUser("unconfirmed@example.test");
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Users.Single(u => u.Id == unconfirmed.Id).EmailConfirmed = false;
            ctx.SaveChanges();
        }
        Subscribe(unconfirmed, feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.WithSequence(1));

        var result = await CreateService().PollAsync();

        Assert.Equal("email-on@example.test", Assert.Single(result).Key);
        Assert.Single(StoredArticles());
    }

    [Fact]
    public async Task PollAsync_SubscriberOfSeveralFeeds_GetsOneCombinedList()
    {
        var bbc = AddFeed("bbc");
        var dw = AddFeed("dw");
        var alice = _db.AddUser("alice@example.test");
        Subscribe(alice, bbc);
        Subscribe(alice, dw);
        Subscribe(_db.AddUser("bob@example.test"), dw);
        _fetcher.Serve(bbc.FeedUrl, RssDocument.With(("b1", "BBC one", null)));
        _fetcher.Serve(dw.FeedUrl, RssDocument.With(("d1", "DW one", null)));

        var result = await CreateService().PollAsync();

        Assert.Equal(["BBC one", "DW one"], result["alice@example.test"].Select(a => a.Title).Order());
        Assert.Equal(["DW one"], result["bob@example.test"].Select(a => a.Title));
    }

    [Fact]
    public async Task PollAsync_SameGuidInDifferentFeeds_IsStoredPerFeed()
    {
        var bbc = AddFeed("bbc");
        var dw = AddFeed("dw");
        var alice = _db.AddUser();
        Subscribe(alice, bbc);
        Subscribe(alice, dw);
        _fetcher.Serve(bbc.FeedUrl, RssDocument.With(("shared-guid", "From BBC", null)));
        _fetcher.Serve(dw.FeedUrl, RssDocument.With(("shared-guid", "From DW", null)));

        await CreateService().PollAsync();

        Assert.Equal(["From BBC", "From DW"], StoredArticles().Select(a => a.Title).Order());
    }

    [Fact]
    public async Task PollAsync_DuplicateGuidWithinOneDocument_IsStoredOnce()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        _fetcher.Serve(feed.FeedUrl, RssDocument.With(("dup", "First copy", null), ("dup", "Second copy", null)));

        var result = await CreateService().PollAsync();

        Assert.Equal("First copy", Assert.Single(StoredArticles()).Title);
        Assert.Single(Assert.Single(result).Value);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("parse")]
    [InlineData("throw")]
    public async Task PollAsync_OneBrokenFeed_DoesNotStopTheOthers(string failure)
    {
        var broken = AddFeed("a-broken");
        var healthy = AddFeed("b-healthy");
        var alice = _db.AddUser();
        Subscribe(alice, broken);
        Subscribe(alice, healthy);
        switch (failure)
        {
            case "fetch": _fetcher.Fail(broken.FeedUrl); break;
            case "parse": _fetcher.Serve(broken.FeedUrl, "<html>definitely not a feed</html>"); break;
            default: _fetcher.Throw(broken.FeedUrl, new InvalidOperationException("boom")); break;
        }
        _fetcher.Serve(healthy.FeedUrl, RssDocument.WithSequence(1));

        var result = await CreateService().PollAsync();

        Assert.Equal([broken.FeedUrl, healthy.FeedUrl], _fetcher.RequestedUrls);
        Assert.Equal("Article 1", Assert.Single(Assert.Single(result).Value).Title);
        Assert.Contains(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PollAsync_Cancelled_Throws()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().PollAsync(cts.Token));
    }

    [Fact]
    public async Task PollAsync_FitsOverlongFieldsToTheColumns()
    {
        var feed = AddFeed("bbc");
        Subscribe(_db.AddUser(), feed);
        var longTitle = new string('t', Article.TitleMaxLength + 50);
        var longGuid = new string('g', Article.ExternalIdMaxLength + 50);
        var longLink = "https://articles.example.test/" + new string('l', Article.UrlMaxLength);
        _fetcher.Serve(feed.FeedUrl, $"""
            <rss version="2.0"><channel>
              <item><guid isPermaLink="false">{longGuid}</guid><title>{longTitle}</title><link>https://articles.example.test/ok</link></item>
              <item><guid isPermaLink="false">dropped</guid><title>Unusable link</title><link>{longLink}</link></item>
            </channel></rss>
            """);

        await CreateService().PollAsync();

        var article = Assert.Single(StoredArticles());
        Assert.Equal(Article.TitleMaxLength, article.Title.Length);
        Assert.Equal(Article.ExternalIdMaxLength, article.ExternalId.Length);
    }

}
