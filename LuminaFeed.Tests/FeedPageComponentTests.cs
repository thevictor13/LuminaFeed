using System.Security.Claims;
using Bunit;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using FeedPage = LuminaFeed.Components.Pages.Feed;

namespace LuminaFeed.Tests;

/// <summary>
/// The B4 feed detail page, rendered with bUnit over the <b>real services</b> on in-memory SQLite: the header, the
/// article cards, and the interactive subscribe button (anonymous → login link; signed-in → red Unsubscribe that
/// persists). The persisted-state property has no supplier here, so the page takes its service-loading path.
/// </summary>
public sealed class FeedPageComponentTests : BunitContext
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Feed _feed;

    public FeedPageComponentTests()
    {
        Services.AddSingleton<IFeedService>(
            new FeedService(_db, new CreateFeedRequestValidator(), new UpdateFeedRequestValidator()));
        Services.AddSingleton<ISubscriptionService>(new SubscriptionService(_db));
        _feed = _db.AddFeed(_db.AddCategory("World News").Id, "BBC News");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _db.Dispose();
    }

    private Article AddArticle(Guid feedId, string title, string? summary = null)
    {
        using var ctx = _db.CreateDbContext();
        var article = new Article
        {
            FeedId = feedId,
            ExternalId = title,
            Title = title,
            Link = $"https://articles.test/{title}",
            Summary = summary,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        ctx.Articles.Add(article);
        ctx.SaveChanges();
        return article;
    }

    private ApplicationUser SignInAs(string email)
    {
        var user = _db.AddUser(email);
        AddAuthorization().SetAuthorized(email).SetClaims(new Claim(ClaimTypes.NameIdentifier, user.Id));
        return user;
    }

    private IRenderedComponent<FeedPage> RenderFeed(Guid feedId)
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Render<FeedPage>(ps => ps.Add(p => p.FeedId, feedId));
    }

    private static string SubscribeButton => "button[aria-label='Subscribe to BBC News']";
    private static string UnsubscribeButton => "button[aria-label='Unsubscribe from BBC News']";

    [Fact]
    public void Anonymous_RendersHeader_ArticleCards_AndSubscribeAsALoginLink()
    {
        AddAuthorization().SetNotAuthorized();
        AddArticle(_feed.Id, "Headline one", "First paragraph.");
        AddArticle(_feed.Id, "Headline two");

        var cut = RenderFeed(_feed.Id);

        var heading = cut.WaitForElement("h1");
        Assert.Equal("BBC News", heading.TextContent.Trim());
        Assert.Equal(2, cut.FindAll(".article-card").Count);
        // A Visit site link to the publisher (external).
        Assert.NotEmpty(cut.FindAll("a[target='_blank']"));
        // Subscribe is a login link carrying a ReturnUrl (anonymous visitors have no subscriptions).
        var subscribe = cut.Find("a[aria-label='Subscribe to BBC News']");
        Assert.Contains("ReturnUrl", subscribe.GetAttribute("href"));
    }

    [Fact]
    public async Task SignedIn_ClickingSubscribe_TurnsItIntoRedUnsubscribe_AndPersists()
    {
        var alice = SignInAs("alice@example.test");

        var cut = RenderFeed(_feed.Id);
        var button = cut.WaitForElement(SubscribeButton);
        Assert.Contains("btn-primary", button.ClassList);

        await button.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("btn-danger", cut.Find(UnsubscribeButton).ClassList));
        using var ctx = _db.CreateDbContext();
        var row = Assert.Single(ctx.Subscriptions);
        Assert.Equal(alice.Id, row.UserId);
        Assert.Equal(_feed.Id, row.FeedId);
        Assert.True(row.EmailEnabled);
    }

    [Fact]
    public void SignedIn_AlreadySubscribed_ShowsRedUnsubscribe()
    {
        var alice = SignInAs("alice@example.test");
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription { UserId = alice.Id, FeedId = _feed.Id, EmailEnabled = true });
            ctx.SaveChanges();
        }

        var cut = RenderFeed(_feed.Id);

        var button = cut.WaitForElement(UnsubscribeButton);
        Assert.Contains("btn-danger", button.ClassList);
    }

    [Fact]
    public void UnknownFeed_ShowsNotFound()
    {
        AddAuthorization().SetNotAuthorized();

        var cut = RenderFeed(Guid.CreateVersion7());

        cut.WaitForAssertion(() => Assert.Contains("Feed not found", cut.Markup));
    }

    [Fact]
    public void FeedWithoutArticles_ShowsTheEmptyState()
    {
        AddAuthorization().SetNotAuthorized();

        var cut = RenderFeed(_feed.Id);

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".article-card"));
            Assert.Contains("No articles", cut.Markup);
        });
    }
}
