using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Users;

namespace LuminaFeed.Tests;

/// <summary>Covers A3 (admin user management: list, remove a subscription, delete a registration) against real in-memory SQLite.</summary>
public sealed class UserAdminServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private UserAdminService CreateService() => new(_db);

    public void Dispose() => _db.Dispose();

    private Feed AddFeed(string name) => _db.AddFeed(_db.AddCategory(name + " category").Id, name);

    private void Subscribe(string userId, Guid feedId, bool email = true, bool slack = false, string? webhook = null)
    {
        using var ctx = _db.CreateDbContext();
        ctx.Subscriptions.Add(new Subscription
        {
            UserId = userId,
            FeedId = feedId,
            EmailEnabled = email,
            SlackEnabled = slack,
            SlackWebhookUrl = webhook,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task ListAsync_ReturnsUsersOrderedByEmail_WithTheirSubscriptions()
    {
        var alice = _db.AddUser("alice@example.test");
        var bob = _db.AddUser("bob@example.test");
        var bbc = AddFeed("BBC News");
        var dw = AddFeed("DW");
        Subscribe(alice.Id, bbc.Id, email: true, slack: false);
        Subscribe(alice.Id, dw.Id, email: false, slack: true, webhook: "https://hooks.slack.com/services/T/B/X");
        Subscribe(bob.Id, bbc.Id);

        var users = await CreateService().ListAsync();

        Assert.Equal(["alice@example.test", "bob@example.test"], users.Select(u => u.Email));

        var aliceSummary = users[0];
        // Subscriptions are ordered by feed name: BBC News, then DW.
        Assert.Equal([bbc.Id, dw.Id], aliceSummary.Subscriptions.Select(s => s.FeedId));
        Assert.Equal(["BBC News", "DW"], aliceSummary.Subscriptions.Select(s => s.FeedName));
        Assert.True(aliceSummary.Subscriptions[0].EmailEnabled);
        Assert.False(aliceSummary.Subscriptions[0].SlackEnabled);
        Assert.False(aliceSummary.Subscriptions[1].EmailEnabled);
        Assert.True(aliceSummary.Subscriptions[1].SlackEnabled);

        Assert.Equal([bbc.Id], users[1].Subscriptions.Select(s => s.FeedId));
    }

    [Fact]
    public async Task ListAsync_UserWithNoSubscriptions_HasAnEmptyList()
    {
        _db.AddUser("loner@example.test");

        var user = Assert.Single(await CreateService().ListAsync());

        Assert.Equal("loner@example.test", user.Email);
        Assert.Empty(user.Subscriptions);
    }

    [Fact]
    public async Task RemoveSubscriptionAsync_RemovesOnlyThatUsersSubscriptionToThatFeed()
    {
        var alice = _db.AddUser("alice@example.test");
        var bob = _db.AddUser("bob@example.test");
        var bbc = AddFeed("BBC News");
        var dw = AddFeed("DW");
        Subscribe(alice.Id, bbc.Id);
        Subscribe(alice.Id, dw.Id);
        Subscribe(bob.Id, bbc.Id);

        var result = await CreateService().RemoveSubscriptionAsync(alice.Id, bbc.Id);

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        Assert.Equal([dw.Id], ctx.Subscriptions.Where(s => s.UserId == alice.Id).Select(s => s.FeedId).ToList());
        Assert.Equal([bbc.Id], ctx.Subscriptions.Where(s => s.UserId == bob.Id).Select(s => s.FeedId).ToList());
    }

    [Fact]
    public async Task RemoveSubscriptionAsync_WhenNotSubscribed_IsNotFound()
    {
        var user = _db.AddUser();
        var feed = AddFeed("BBC News");

        var result = await CreateService().RemoveSubscriptionAsync(user.Id, feed.Id);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(UserErrors.SubscriptionNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task DeleteUserAsync_DeletesTheUser_AndCascadesTheirSubscriptions()
    {
        var alice = _db.AddUser("alice@example.test");
        var bob = _db.AddUser("bob@example.test");
        var bbc = AddFeed("BBC News");
        Subscribe(alice.Id, bbc.Id);
        Subscribe(bob.Id, bbc.Id);
        using (var seed = _db.CreateDbContext())
        {
            seed.Articles.Add(new Article { FeedId = bbc.Id, ExternalId = "a1", Title = "t1", Link = "https://bbc.test/1" });
            seed.SaveChanges();
        }

        var result = await CreateService().DeleteUserAsync(alice.Id, "some-other-admin");

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        Assert.Null(ctx.Users.FirstOrDefault(u => u.Id == alice.Id));
        Assert.Empty(ctx.Subscriptions.Where(s => s.UserId == alice.Id));
        // Another user's subscription and the feed's articles are untouched (articles hang off the feed, not the user).
        Assert.NotNull(ctx.Users.FirstOrDefault(u => u.Id == bob.Id));
        Assert.Single(ctx.Subscriptions.Where(s => s.UserId == bob.Id));
        Assert.Single(ctx.Articles);
    }

    [Fact]
    public async Task DeleteUserAsync_UnknownUser_IsNotFound()
    {
        var result = await CreateService().DeleteUserAsync("no-such-user", "some-admin");

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(UserErrors.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task DeleteUserAsync_OfYourOwnAccount_IsRefused_AndTheUserRemains()
    {
        var admin = _db.AddUser("admin@example.test");

        var result = await CreateService().DeleteUserAsync(admin.Id, admin.Id);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(UserErrors.CannotDeleteSelf.Code, result.FirstError.Code);
        using var ctx = _db.CreateDbContext();
        Assert.NotNull(ctx.Users.FirstOrDefault(u => u.Id == admin.Id));
    }
}
