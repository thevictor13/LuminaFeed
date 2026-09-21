using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Subscriptions;

namespace LuminaFeed.Tests;

/// <summary>Covers S3 (email-only subscribe + minimal unsubscribe) against real in-memory SQLite.</summary>
public sealed class SubscriptionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private SubscriptionService CreateService() => new(_db);

    public void Dispose() => _db.Dispose();

    private Feed AddFeed(string name = "BBC News") => _db.AddFeed(_db.AddCategory(name + " category").Id, name);

    [Fact]
    public async Task SubscribeByEmailAsync_PersistsAnEmailOnlySubscription()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SubscribeByEmailAsync(user.Id, feed.Id);

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Subscriptions);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal(feed.Id, stored.FeedId);
        Assert.True(stored.EmailEnabled);
        Assert.False(stored.SlackEnabled);
        Assert.Null(stored.SlackWebhookUrl);
        Assert.Equal(7, stored.Id.Version);
    }

    [Fact]
    public async Task SubscribeByEmailAsync_Twice_IsIdempotent()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();

        Assert.False((await service.SubscribeByEmailAsync(user.Id, feed.Id)).IsError);
        Assert.False((await service.SubscribeByEmailAsync(user.Id, feed.Id)).IsError);

        using var ctx = _db.CreateDbContext();
        Assert.Single(ctx.Subscriptions);
    }

    [Fact]
    public async Task SubscribeByEmailAsync_ReEnablesEmail_AndKeepsTheSlackSettings()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription
            {
                UserId = user.Id,
                FeedId = feed.Id,
                EmailEnabled = false,
                SlackEnabled = true,
                SlackWebhookUrl = "https://hooks.slack.com/services/T000/B000/XXXX",
            });
            ctx.SaveChanges();
        }

        var result = await CreateService().SubscribeByEmailAsync(user.Id, feed.Id);

        Assert.False(result.IsError);
        using var verify = _db.CreateDbContext();
        var stored = Assert.Single(verify.Subscriptions);
        Assert.True(stored.EmailEnabled);
        Assert.True(stored.SlackEnabled);
        Assert.Equal("https://hooks.slack.com/services/T000/B000/XXXX", stored.SlackWebhookUrl);
    }

    [Fact]
    public async Task SubscribeByEmailAsync_UnknownFeed_IsNotFound()
    {
        var user = _db.AddUser();

        var result = await CreateService().SubscribeByEmailAsync(user.Id, Guid.CreateVersion7());

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task SubscribeByEmailAsync_UnknownUser_IsNotFound_AndNothingIsStored()
    {
        var feed = AddFeed();

        var result = await CreateService().SubscribeByEmailAsync("no-such-user", feed.Id);

        Assert.True(result.IsError);
        Assert.Equal(SubscriptionErrors.UserNotFound.Code, result.FirstError.Code);
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Subscriptions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnonymousUser_CannotSubscribeOrUnsubscribe_AndHasNoSubscriptions(string? userId)
    {
        var feed = AddFeed();
        var service = CreateService();

        var subscribe = await service.SubscribeByEmailAsync(userId, feed.Id);
        var unsubscribe = await service.UnsubscribeAsync(userId, feed.Id);

        Assert.Equal(SubscriptionErrors.UserRequired.Code, subscribe.FirstError.Code);
        Assert.Equal(ErrorType.Validation, subscribe.FirstError.Type);
        Assert.Equal(SubscriptionErrors.UserRequired.Code, unsubscribe.FirstError.Code);
        Assert.Empty(await service.GetSubscribedFeedIdsAsync(userId));
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesOnlyThatUsersSubscriptionToThatFeed()
    {
        var alice = _db.AddUser("alice@example.test");
        var bob = _db.AddUser("bob@example.test");
        var bbc = AddFeed("BBC News");
        var dw = AddFeed("DW");
        var service = CreateService();
        await service.SubscribeByEmailAsync(alice.Id, bbc.Id);
        await service.SubscribeByEmailAsync(alice.Id, dw.Id);
        await service.SubscribeByEmailAsync(bob.Id, bbc.Id);

        var result = await service.UnsubscribeAsync(alice.Id, bbc.Id);

        Assert.False(result.IsError);
        Assert.Equal([dw.Id], await service.GetSubscribedFeedIdsAsync(alice.Id));
        Assert.Equal([bbc.Id], await service.GetSubscribedFeedIdsAsync(bob.Id));
    }

    [Fact]
    public async Task UnsubscribeAsync_WhenNotSubscribed_IsNotFound()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().UnsubscribeAsync(user.Id, feed.Id);

        Assert.True(result.IsError);
        Assert.Equal(SubscriptionErrors.NotSubscribed.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task SubscribeAfterUnsubscribe_CreatesAFreshSubscription()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();
        await service.SubscribeByEmailAsync(user.Id, feed.Id);
        await service.UnsubscribeAsync(user.Id, feed.Id);

        var result = await service.SubscribeByEmailAsync(user.Id, feed.Id);

        Assert.False(result.IsError);
        Assert.Equal([feed.Id], await service.GetSubscribedFeedIdsAsync(user.Id));
    }

    [Fact]
    public async Task GetSubscribedFeedIdsAsync_ReturnsOnlyThatUsersFeeds()
    {
        var alice = _db.AddUser("alice@example.test");
        var bob = _db.AddUser("bob@example.test");
        var bbc = AddFeed("BBC News");
        var dw = AddFeed("DW");
        var service = CreateService();
        await service.SubscribeByEmailAsync(alice.Id, bbc.Id);
        await service.SubscribeByEmailAsync(bob.Id, dw.Id);

        Assert.Equal([bbc.Id], await service.GetSubscribedFeedIdsAsync(alice.Id));
        Assert.Equal([dw.Id], await service.GetSubscribedFeedIdsAsync(bob.Id));
        Assert.Empty(await service.GetSubscribedFeedIdsAsync("no-such-user"));
    }
}
