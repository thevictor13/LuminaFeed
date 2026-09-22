using System.Security.Claims;
using Bunit;
using LuminaFeed.Components.Pages;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Feed = LuminaFeed.Domain.Feed;

namespace LuminaFeed.Tests;

/// <summary>
/// The public list rendered with bUnit over the <b>real services</b> on in-memory SQLite: the wiring the page tests
/// (prerendered HTML only) cannot reach — the anonymous login link, the B3 category filter, and that a card button
/// opens the channel dialog and the card reflects its result. The dialog's own behaviour (switches, webhook
/// validation, in-flight, errors) is covered by <see cref="SubscribeDialogComponentTests"/>; the paging/order by
/// <see cref="CategorySectionComponentTests"/>. The persisted-state properties have no supplier here, so the page
/// takes its service-loading path.
/// </summary>
public sealed class HomePageComponentTests : BunitContext
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Feed _feed;

    public HomePageComponentTests()
    {
        Services.AddSingleton<IFeedService>(
            new FeedService(_db, new CreateFeedRequestValidator(), new UpdateFeedRequestValidator()));
        Services.AddSingleton<ISubscriptionService>(new SubscriptionService(_db, new SaveSubscriptionRequestValidator()));
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

    private ApplicationUser SignInAs(string email)
    {
        var user = _db.AddUser(email);
        AddAuthorization().SetAuthorized(email).SetClaims(new Claim(ClaimTypes.NameIdentifier, user.Id));
        return user;
    }

    private const string SubscribeButton = "button[aria-label='Subscribe to BBC News']";
    private const string UnsubscribeButton = "button[aria-label='Unsubscribe from BBC News']";

    [Fact]
    public void Anonymous_SeesSubscribeAsALoginLink_CarryingTheReturnUrl()
    {
        AddAuthorization().SetNotAuthorized();

        var cut = RenderHome();

        var link = cut.WaitForElement("a[aria-label='Subscribe to BBC News']");
        Assert.Equal("Account/Login?ReturnUrl=%2F", link.GetAttribute("href"));
        Assert.Contains("btn-primary", link.ClassList);
        // The subscribe control is a login link, not a button (the order-menu / filter triggers are other buttons).
        Assert.Empty(cut.FindAll("button[aria-label^='Subscribe'], button[aria-label^='Unsubscribe']"));
    }

    [Fact]
    public async Task SignedIn_ClickingSubscribe_OpensTheChannelDialog()
    {
        SignInAs("alice@example.test");
        var cut = RenderHome();

        await cut.WaitForElement(SubscribeButton).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            Assert.Contains("Subscribe to BBC News", cut.Find("#sub-dialog-title").TextContent));
    }

    [Fact]
    public async Task SavingTheDialog_TurnsTheCardButtonRed_AndPersistsTheSubscription()
    {
        var alice = SignInAs("alice@example.test");
        var cut = RenderHome();
        await cut.WaitForElement(SubscribeButton).ClickAsync(new MouseEventArgs());

        await cut.WaitForElement(".modal form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            var toggled = cut.Find(UnsubscribeButton);
            Assert.Contains("btn-danger", toggled.ClassList);
        });
        Assert.Empty(cut.FindAll(".modal")); // the dialog closed after saving
        using var ctx = _db.CreateDbContext();
        var row = Assert.Single(ctx.Subscriptions);
        Assert.Equal(alice.Id, row.UserId);
        Assert.Equal(_feed.Id, row.FeedId);
        Assert.True(row.EmailEnabled);
    }

    [Fact]
    public async Task SignedIn_Unsubscribing_ThroughTheDialog_RevertsTheButton_AndRemovesTheRow()
    {
        var alice = SignInAs("alice@example.test");
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Subscriptions.Add(new Subscription { UserId = alice.Id, FeedId = _feed.Id, EmailEnabled = true });
            ctx.SaveChanges();
        }

        var cut = RenderHome();
        await cut.WaitForElement(UnsubscribeButton).ClickAsync(new MouseEventArgs());   // red card button opens the dialog
        await cut.WaitForElement(".modal-footer .btn-danger").ClickAsync(new MouseEventArgs()); // dialog's Unsubscribe

        cut.WaitForAssertion(() => Assert.Contains("btn-primary", cut.Find(SubscribeButton).ClassList));
        Assert.Empty(cut.FindAll(".modal"));
        using var check = _db.CreateDbContext();
        Assert.Empty(check.Subscriptions);
    }

    [Fact]
    public async Task CancellingTheDialog_LeavesTheSubscriptionUnchanged()
    {
        SignInAs("alice@example.test");
        var cut = RenderHome();
        await cut.WaitForElement(SubscribeButton).ClickAsync(new MouseEventArgs());
        cut.WaitForElement(".modal");

        await cut.Find(".modal-footer .btn-outline-secondary").ClickAsync(new MouseEventArgs()); // Cancel

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".modal")));
        Assert.Contains("btn-primary", cut.Find(SubscribeButton).ClassList); // still "Subscribe"
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Subscriptions);
    }

    // --- B3: the category filter ------------------------------------------------------------------

    [Fact]
    public async Task SelectingACategory_ShowsOnlyThatCategory_AtTheDeeperCap()
    {
        AddAuthorization().SetNotAuthorized();
        // A second category with more than five feeds, so the filter is observable and its deeper (30) cap shows.
        var techId = _db.AddCategory("Technology").Id;
        for (var i = 1; i <= 6; i++)
            _db.AddFeed(techId, $"Tech Daily {i:D2}");

        var cut = RenderHome();
        cut.WaitForAssertion(() =>
        {
            var headers = cut.FindAll("section h2").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("World News", headers);
            Assert.Contains("Technology", headers);
        });
        // Unfiltered, Technology is capped at five with a More button.
        Assert.NotEmpty(cut.FindAll("button[aria-label='Show more Technology feeds']"));

        await cut.Find("button[aria-label='Filter feeds by category']").ClickAsync(new MouseEventArgs());
        var tech = cut.FindAll(".dropdown-menu.show .dropdown-item").Single(b => b.TextContent.Trim() == "Technology");
        await tech.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            // Only the chosen category remains, now showing all six (deeper cap) with no More button.
            Assert.Equal(["Technology"], cut.FindAll("section h2").Select(h => h.TextContent.Trim()));
            Assert.Equal(6, cut.FindAll(".feed-card").Count);
            Assert.Empty(cut.FindAll("button[aria-label='Show more Technology feeds']"));
        });
    }

    [Fact]
    public async Task ClearingTheFilter_RestoresAllCategories_AtTheDefaultCap()
    {
        AddAuthorization().SetNotAuthorized();
        var techId = _db.AddCategory("Technology").Id;
        for (var i = 1; i <= 6; i++)
            _db.AddFeed(techId, $"Tech Daily {i:D2}");

        var cut = RenderHome();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("section h2").Count));

        // Narrow to Technology…
        await cut.Find("button[aria-label='Filter feeds by category']").ClickAsync(new MouseEventArgs());
        var tech = cut.FindAll(".dropdown-menu.show .dropdown-item").Single(b => b.TextContent.Trim() == "Technology");
        await tech.ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            Assert.Equal(["Technology"], cut.FindAll("section h2").Select(h => h.TextContent.Trim())));

        // …then clear back to "All feeds".
        await cut.Find("button[aria-label='Filter feeds by category']").ClickAsync(new MouseEventArgs());
        var all = cut.FindAll(".dropdown-menu.show .dropdown-item").Single(b => b.TextContent.Trim() == "All feeds");
        await all.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var headers = cut.FindAll("section h2").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("World News", headers);
            Assert.Contains("Technology", headers);
            // Technology is back to the default cap of five, with its More button.
            Assert.NotEmpty(cut.FindAll("button[aria-label='Show more Technology feeds']"));
        });
    }
}
