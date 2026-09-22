using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Subscriptions;

namespace LuminaFeed.Tests;

/// <summary>
/// Covers the subscription service against real in-memory SQLite: the S3 email-only path plus the C1 channel
/// upsert (<see cref="SubscriptionService.SaveSubscriptionAsync"/>), the Slack-webhook validation, and the
/// dialog's edit state / webhook prefill (<see cref="SubscriptionService.GetSubscriptionForEditAsync"/>).
/// </summary>
public sealed class SubscriptionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private SubscriptionService CreateService() => new(_db, new SaveSubscriptionRequestValidator());

    private const string ValidWebhook = "https://hooks.slack.com/services/T000/B000/XXXXXXXX";

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

    // --- C1: SaveSubscriptionAsync (channel upsert) --------------------------------------------------

    [Fact]
    public async Task SaveSubscriptionAsync_CreatesAnEmailOnlySubscription()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: true, SlackEnabled: false));

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Subscriptions);
        Assert.True(stored.EmailEnabled);
        Assert.False(stored.SlackEnabled);
        Assert.Null(stored.SlackWebhookUrl);
        Assert.Equal(7, stored.Id.Version);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_WithSlack_PersistsTheTrimmedWebhook()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: true, SlackEnabled: true, $"  {ValidWebhook}  "));

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Subscriptions);
        Assert.True(stored.SlackEnabled);
        Assert.Equal(ValidWebhook, stored.SlackWebhookUrl);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_UpdatesChannelsOnTheExistingRow()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();
        await service.SaveSubscriptionAsync(user.Id, feed.Id, new SaveSubscriptionRequest(true, false));

        var result = await service.SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: false, SlackEnabled: true, ValidWebhook));

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Subscriptions); // still one row — an upsert, not a second subscription
        Assert.False(stored.EmailEnabled);
        Assert.True(stored.SlackEnabled);
        Assert.Equal(ValidWebhook, stored.SlackWebhookUrl);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_TurningOneChannelOff_KeepsTheSubscription()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();
        await service.SaveSubscriptionAsync(user.Id, feed.Id, new SaveSubscriptionRequest(true, true, ValidWebhook));

        // Per-channel unsubscribe: email off, Slack still on — the row survives.
        var result = await service.SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: false, SlackEnabled: true, ValidWebhook));

        Assert.False(result.IsError);
        Assert.Equal([feed.Id], await service.GetSubscribedFeedIdsAsync(user.Id));
    }

    [Fact]
    public async Task SaveSubscriptionAsync_SlackOff_PreservesTheStoredWebhookForPrefill()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();
        await service.SaveSubscriptionAsync(user.Id, feed.Id, new SaveSubscriptionRequest(true, true, ValidWebhook));

        // Slack switched off, no webhook supplied — the previous webhook stays so it can be prefilled later.
        await service.SaveSubscriptionAsync(user.Id, feed.Id, new SaveSubscriptionRequest(true, false));

        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Subscriptions);
        Assert.False(stored.SlackEnabled);
        Assert.Equal(ValidWebhook, stored.SlackWebhookUrl);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_NoChannel_IsAValidationError_AndStoresNothing()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: false, SlackEnabled: false));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(result.Errors, e => e.Description == "Enable at least one notification channel, or unsubscribe.");
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Subscriptions);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_SlackOnWithoutWebhook_IsAValidationError()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: false, SlackEnabled: true, SlackWebhookUrl: "  "));

        Assert.True(result.IsError);
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        Assert.Contains(result.Errors, e => e.Code == nameof(SaveSubscriptionRequest.SlackWebhookUrl));
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T/B/X")]  // not https
    [InlineData("https://hooks.slack.com/serviceX/T/B/X")] // wrong path
    [InlineData("https://evil.example.com/services/T/B/X")] // wrong host
    [InlineData("not-a-url")]
    public async Task SaveSubscriptionAsync_SlackOnWithNonSlackWebhook_IsRejected(string webhook)
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: true, SlackEnabled: true, webhook));

        Assert.True(result.IsError);
        Assert.Contains(
            result.Errors,
            e => e.Type == ErrorType.Validation && e.Code == nameof(SaveSubscriptionRequest.SlackWebhookUrl));
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Subscriptions);
    }

    [Fact]
    public async Task SaveSubscriptionAsync_WebhookOverTheColumnLimit_IsRejected()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var tooLong = Subscription.SlackWebhookUrlPrefix + "/" + new string('a', Subscription.SlackWebhookUrlMaxLength);

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, feed.Id, new SaveSubscriptionRequest(EmailEnabled: true, SlackEnabled: true, tooLong));

        Assert.True(result.IsError);
        Assert.Contains(
            result.Errors,
            e => e.Type == ErrorType.Validation && e.Code == nameof(SaveSubscriptionRequest.SlackWebhookUrl));
    }

    [Fact]
    public async Task SaveSubscriptionAsync_UnknownFeed_IsNotFound()
    {
        var user = _db.AddUser();

        var result = await CreateService().SaveSubscriptionAsync(
            user.Id, Guid.CreateVersion7(), new SaveSubscriptionRequest(true, false));

        Assert.True(result.IsError);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SaveSubscriptionAsync_AnonymousUser_IsRejected(string? userId)
    {
        var feed = AddFeed();

        var result = await CreateService().SaveSubscriptionAsync(userId, feed.Id, new SaveSubscriptionRequest(true, false));

        Assert.Equal(SubscriptionErrors.UserRequired.Code, result.FirstError.Code);
    }

    // --- C1: GetSubscriptionForEditAsync (dialog state + webhook prefill) ---------------------------

    [Fact]
    public async Task GetSubscriptionForEditAsync_NotSubscribed_ReturnsDefaults()
    {
        var user = _db.AddUser();
        var feed = AddFeed();

        var result = await CreateService().GetSubscriptionForEditAsync(user.Id, feed.Id);

        Assert.False(result.IsError);
        var state = result.Value;
        Assert.False(state.Exists);
        Assert.True(state.EmailEnabled);   // email defaults on for a new subscription
        Assert.False(state.SlackEnabled);
        Assert.Null(state.WebhookPrefill);
    }

    [Fact]
    public async Task GetSubscriptionForEditAsync_Subscribed_ReflectsTheStoredState()
    {
        var user = _db.AddUser();
        var feed = AddFeed();
        var service = CreateService();
        await service.SaveSubscriptionAsync(user.Id, feed.Id, new SaveSubscriptionRequest(false, true, ValidWebhook));

        var result = await service.GetSubscriptionForEditAsync(user.Id, feed.Id);

        Assert.False(result.IsError);
        var state = result.Value;
        Assert.True(state.Exists);
        Assert.False(state.EmailEnabled);
        Assert.True(state.SlackEnabled);
        Assert.Equal(ValidWebhook, state.WebhookPrefill);
    }

    [Fact]
    public async Task GetSubscriptionForEditAsync_PrefillsFromTheUsersMostRecentWebhook()
    {
        var user = _db.AddUser();
        var withWebhook = AddFeed("BBC News");
        var fresh = AddFeed("DW");
        var service = CreateService();
        await service.SaveSubscriptionAsync(user.Id, withWebhook.Id, new SaveSubscriptionRequest(true, true, ValidWebhook));

        // A different feed with no Slack yet still offers the user's last webhook as the prefill (spec: LastOrDefault).
        var result = await service.GetSubscriptionForEditAsync(user.Id, fresh.Id);

        Assert.False(result.IsError);
        Assert.False(result.Value.Exists);
        Assert.Equal(ValidWebhook, result.Value.WebhookPrefill);
    }

    [Fact]
    public async Task GetSubscriptionForEditAsync_UnknownFeed_IsNotFound()
    {
        var user = _db.AddUser();

        var result = await CreateService().GetSubscriptionForEditAsync(user.Id, Guid.CreateVersion7());

        Assert.True(result.IsError);
        Assert.Equal("Feed.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task GetSubscriptionForEditAsync_UnknownUser_IsNotFound()
    {
        var feed = AddFeed();

        var result = await CreateService().GetSubscriptionForEditAsync("no-such-user", feed.Id);

        Assert.True(result.IsError);
        Assert.Equal(SubscriptionErrors.UserNotFound.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetSubscriptionForEditAsync_AnonymousUser_IsRejected(string? userId)
    {
        var feed = AddFeed();

        var result = await CreateService().GetSubscriptionForEditAsync(userId, feed.Id);

        Assert.Equal(SubscriptionErrors.UserRequired.Code, result.FirstError.Code);
    }
}
