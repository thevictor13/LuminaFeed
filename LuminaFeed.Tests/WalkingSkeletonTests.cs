using LuminaFeed.Data;
using LuminaFeed.Services.Categories;
using LuminaFeed.Services.Email;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Polling;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuminaFeed.Tests;

/// <summary>
/// The Phase 1 definition of done, end to end through the <b>real host and DI graph</b>: an admin adds a feed →
/// it appears publicly → a signed-in user subscribes → polling fetches new articles → the user receives an email.
/// Only the two outside-world edges are doubles: the HTTP fetcher (canned RSS) and the SMTP transport (recorded).
/// The polling loop is driven by hand (the test host doesn't run it on a timer), so every pass is the test's own.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class WalkingSkeletonTests
{
    private const string FeedUrl = "https://gazette.example.test/rss.xml";

    [Fact]
    public async Task AdminAddsFeed_UserSubscribes_PollingFetches_UserIsEmailed()
    {
        var fetcher = new FakeFeedFetcher();
        using var factory = new TestAppFactory(seedAdmin: true, configureServices: services =>
        {
            services.RemoveAll<IFeedFetcher>();
            services.AddSingleton<IFeedFetcher>(fetcher);
        });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var mail = Assert.IsType<RecordingMailSender>(services.GetRequiredService<IMailSender>());
        // The real loop class over the real DI graph, just not started on its timer.
        var pollingLoop = ActivatorUtilities.CreateInstance<FeedPollingBackgroundService>(factory.Services);

        // 1. The admin adds a category and a feed.
        var category = await services.GetRequiredService<ICategoryService>()
            .CreateAsync(new CreateCategoryRequest("Skeleton Press", "Walking-skeleton test category."));
        Assert.False(category.IsError);
        var feed = await services.GetRequiredService<IFeedService>().CreateAsync(new CreateFeedRequest(
            "Skeleton Gazette", category.Value.Id, FeedUrl, "https://gazette.example.test"));
        Assert.False(feed.IsError);

        // 2. It appears publicly.
        var home = await client.GetStringAsync("/");
        Assert.Contains("Skeleton Press", home);
        Assert.Contains("Skeleton Gazette", home);

        // 3. A signed-up (confirmed) user subscribes by email.
        var user = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(TestAppFactory.AdminEmail);
        var subscribed = await services.GetRequiredService<ISubscriptionService>().SubscribeByEmailAsync(user!.Id, feed.Value.Id);
        Assert.False(subscribed.IsError);

        // 4. Polling fetches the feed's articles...
        fetcher.Serve(FeedUrl, RssDocument.WithSequence(8));
        await pollingLoop.RunCycleAsync(CancellationToken.None);

        await using (var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(8, await db.Articles.CountAsync(a => a.FeedId == feed.Value.Id));
        }

        // 5. ...and the subscriber receives an email (first poll: the newest five only).
        var firstEmail = Assert.Single(mail.Sent);
        Assert.Equal(TestAppFactory.AdminEmail, firstEmail.ToEmail);
        Assert.Equal("5 new articles from Skeleton Gazette", firstEmail.Subject);
        Assert.Contains("Article 8", firstEmail.HtmlBody);
        Assert.Contains("Article 4", firstEmail.HtmlBody);
        Assert.DoesNotContain("Article 3", firstEmail.HtmlBody);

        // Nothing new → no email. Then the publisher posts one more → exactly that one is announced.
        await pollingLoop.RunCycleAsync(CancellationToken.None);
        Assert.Single(mail.Sent);

        fetcher.Serve(FeedUrl, RssDocument.WithSequence(9));
        await pollingLoop.RunCycleAsync(CancellationToken.None);
        Assert.Equal(2, mail.Sent.Count);
        Assert.Equal("New from Skeleton Gazette: Article 9", mail.Sent[1].Subject);

        // After unsubscribing, the feed is no longer polled and nobody is emailed.
        var unsubscribed = await services.GetRequiredService<ISubscriptionService>().UnsubscribeAsync(user.Id, feed.Value.Id);
        Assert.False(unsubscribed.IsError);
        var requestsBefore = fetcher.RequestedUrls.Count;
        fetcher.Serve(FeedUrl, RssDocument.WithSequence(10));
        await pollingLoop.RunCycleAsync(CancellationToken.None);
        Assert.Equal(requestsBefore, fetcher.RequestedUrls.Count);
        Assert.Equal(2, mail.Sent.Count);
    }
}
