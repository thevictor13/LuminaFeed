using System.Security.Claims;
using Bunit;
using ErrorOr;
using LuminaFeed.Components.Pages;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// The interactive subscribe path of the public list, rendered with bUnit over the <b>real services</b> on in-memory
/// SQLite: what the page tests (prerendered HTML only) cannot reach — the click, the busy gate, the optimistic
/// update and the error alert. The persisted-state properties have no supplier here, so the page takes its
/// service-loading path, which is the one under test.
/// </summary>
public sealed class HomePageComponentTests : BunitContext
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Feed _feed;

    public HomePageComponentTests()
    {
        Services.AddSingleton<IFeedService>(
            new FeedService(_db, new CreateFeedRequestValidator(), new UpdateFeedRequestValidator()));
        _feed = _db.AddFeed(_db.AddCategory("World News").Id, "BBC News");
    }

    /// <summary>
    /// Renders the page as an interactive server renderer would (it declares <c>@rendermode InteractiveServer</c>).
    /// Setting the renderer info locks bUnit's service container, so it happens last.
    /// </summary>
    private IRenderedComponent<Home> RenderHome()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Render<Home>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _db.Dispose();
    }

    private void UseSubscriptions(ISubscriptionService? service = null) =>
        Services.AddSingleton(service ?? new SubscriptionService(_db));

    private ApplicationUser SignInAs(string email)
    {
        var user = _db.AddUser(email);
        AddAuthorization().SetAuthorized(email).SetClaims(new Claim(ClaimTypes.NameIdentifier, user.Id));
        return user;
    }

    private static string SubscribeButton => "button[aria-label='Subscribe to BBC News']";
    private static string UnsubscribeButton => "button[aria-label='Unsubscribe from BBC News']";

    [Fact]
    public void Anonymous_SeesSubscribeAsALoginLink_CarryingTheReturnUrl()
    {
        UseSubscriptions();
        AddAuthorization().SetNotAuthorized();

        var cut = RenderHome();

        var link = cut.WaitForElement("a[aria-label='Subscribe to BBC News']");
        Assert.Equal("Account/Login?ReturnUrl=%2F", link.GetAttribute("href"));
        Assert.Contains("btn-primary", link.ClassList);
        // The subscribe control is a login link, not a button (the order-menu trigger is the page's only button).
        Assert.Empty(cut.FindAll("button[aria-label^='Subscribe'], button[aria-label^='Unsubscribe']"));
    }

    [Fact]
    public async Task SignedIn_ClickingSubscribe_TurnsItIntoRedUnsubscribe_AndPersistsTheSubscription()
    {
        UseSubscriptions();
        var alice = SignInAs("alice@example.test");

        var cut = RenderHome();
        var button = cut.WaitForElement(SubscribeButton);
        Assert.Contains("btn-primary", button.ClassList);

        await button.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var toggled = cut.Find(UnsubscribeButton);
            Assert.Contains("btn-danger", toggled.ClassList);
            Assert.False(toggled.HasAttribute("disabled"));
        });
        Assert.Empty(cut.FindAll(".alert-danger"));
        using var ctx = _db.CreateDbContext();
        var row = Assert.Single(ctx.Subscriptions);
        Assert.Equal(alice.Id, row.UserId);
        Assert.Equal(_feed.Id, row.FeedId);
        Assert.True(row.EmailEnabled);
    }

    [Fact]
    public async Task SignedIn_ClickingUnsubscribe_RevertsTheButton_AndRemovesTheSubscription()
    {
        UseSubscriptions();
        var alice = SignInAs("alice@example.test");
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription { UserId = alice.Id, FeedId = _feed.Id, EmailEnabled = true });
            ctx.SaveChanges();
        }

        var cut = RenderHome();
        var button = cut.WaitForElement(UnsubscribeButton);

        await button.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("btn-primary", cut.Find(SubscribeButton).ClassList));
        using var check = _db.CreateDbContext();
        Assert.Empty(check.Subscriptions);
    }

    [Fact]
    public async Task WhileTheCallIsInFlight_TheButtonIsDisabled()
    {
        var gated = new GatedSubscriptionService(new SubscriptionService(_db));
        UseSubscriptions(gated);
        SignInAs("alice@example.test");
        var cut = RenderHome();
        var button = cut.WaitForElement(SubscribeButton);

        var click = button.ClickAsync(new MouseEventArgs());

        // The handler is parked on the gate: the button re-renders disabled and stays "Subscribe".
        cut.WaitForAssertion(() => Assert.True(cut.Find(SubscribeButton).HasAttribute("disabled")));
        gated.Release();
        await click;
        cut.WaitForAssertion(() => Assert.False(cut.Find(UnsubscribeButton).HasAttribute("disabled")));
    }

    [Fact]
    public async Task WhenTheServiceFails_TheErrorIsShown_AndTheButtonIsUnchanged()
    {
        UseSubscriptions(new FailingSubscriptionService("The database is on fire."));
        SignInAs("alice@example.test");
        var cut = RenderHome();
        var button = cut.WaitForElement(SubscribeButton);

        await button.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("The database is on fire.", cut.Find(".alert-danger").TextContent));
        var unchanged = cut.Find(SubscribeButton);
        Assert.Contains("btn-primary", unchanged.ClassList);
        Assert.False(unchanged.HasAttribute("disabled"));
    }

    /// <summary>Delegates to the real service but holds every subscribe call until released.</summary>
    private sealed class GatedSubscriptionService(ISubscriptionService inner) : ISubscriptionService
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.SetResult();

        public Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(string? userId, CancellationToken cancellationToken = default) =>
            inner.GetSubscribedFeedIdsAsync(userId, cancellationToken);

        public async Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default)
        {
            await _gate.Task;
            return await inner.SubscribeByEmailAsync(userId, feedId, cancellationToken);
        }

        public Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            inner.UnsubscribeAsync(userId, feedId, cancellationToken);
    }

    private sealed class FailingSubscriptionService(string reason) : ISubscriptionService
    {
        public Task<IReadOnlySet<Guid>> GetSubscribedFeedIdsAsync(string? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<ErrorOr<Success>> SubscribeByEmailAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<Success>>(ErrorOr.Error.Failure("Subscription.Failed", reason));

        public Task<ErrorOr<Deleted>> UnsubscribeAsync(string? userId, Guid feedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ErrorOr<Deleted>>(ErrorOr.Error.Failure("Subscription.Failed", reason));
    }
}
