using System.Net;
using LuminaFeed.Data;
using LuminaFeed.Data.Seed;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>Smoke tests for the public pages, served by the real host over the seeded catalogue.</summary>
[Collection(HostCollection.Name)]
public sealed class PublicPagesTests
{
    // Expected counts come from the embedded catalogue, so adding a feed to the research JSON doesn't break these.
    private static readonly int SeededFeeds = SeedCatalogLoader.LoadEmbedded().Feeds.Count;

    // B1: the home page shows at most five cards per category (a "more" button reveals the rest). The prerendered
    // HTML therefore carries the capped total, and one "more" button per category that has more than five feeds.
    private const int CardsPerCategory = 5;
    private static readonly int VisibleCards = SeedCatalogLoader.LoadEmbedded().Feeds
        .GroupBy(f => f.Category).Sum(g => Math.Min(CardsPerCategory, g.Count()));
    private static readonly int CategoriesWithMore = SeedCatalogLoader.LoadEmbedded().Feeds
        .GroupBy(f => f.Category).Count(g => g.Count() > CardsPerCategory);

    [Fact]
    public async Task Home_Anonymous_ShowsSeededFeedsAsCardsGroupedByCategory()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("World News", html);
        Assert.Contains("BBC News", html);
        // At most five cards per category are rendered up front; a "more" button reveals the rest per category.
        Assert.Equal(VisibleCards, CountOf(html, "class=\"card h-100 feed-card\""));
        Assert.Equal(CategoriesWithMore, CountOf(html, "aria-label=\"Show more "));
        // Capping genuinely holds back cards: fewer are shown than the catalogue has, and "more" is offered.
        Assert.True(CategoriesWithMore > 0, "The seeded catalogue should have at least one category with more than five feeds.");
        Assert.True(VisibleCards < SeededFeeds, "Capping should render fewer cards than the full catalogue.");
    }

    [Fact]
    public async Task Home_ShowsAFeedTheAdminJustAdded()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var feeds = scope.ServiceProvider.GetRequiredService<IFeedService>();
            var category = (await feeds.ListByCategoryAsync())[0];
            // Top popularity so it sorts first in its category and lands within the five cards shown up front (B1).
            var created = await feeds.CreateAsync(new CreateFeedRequest(
                "Skeleton Gazette", category.CategoryId, "https://gazette.example.test/rss.xml",
                "https://gazette.example.test", ImageUrl: "https://gazette.example.test/logo.png", Popularity: int.MaxValue));
            Assert.False(created.IsError);
        }

        var html = await client.GetStringAsync("/");

        Assert.Contains("Skeleton Gazette", html);
        Assert.Contains("src=\"https://gazette.example.test/logo.png\"", html);
        Assert.Contains("href=\"https://gazette.example.test\"", html);
    }

    [Fact]
    public async Task Home_Anonymous_SubscribeSendsTheVisitorToLoginAndBack()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        // Anonymous users are treated as having no subscriptions: every visible card offers Subscribe, as a
        // link to the login page carrying the location to return to, and no card shows the red Unsubscribe.
        Assert.Equal(VisibleCards, CountOf(html, ">Subscribe</a>"));
        Assert.Contains("href=\"Account/Login?ReturnUrl=%2F\"", html);
        Assert.Equal(0, CountOf(html, "btn-danger"));
    }

    [Fact]
    public async Task Home_SignedIn_ShowsUnsubscribeInRedOnlyForSubscribedFeeds()
    {
        using var factory = new TestAppFactory(seedAdmin: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(TestAppFactory.AdminEmail);
            var feed = (await scope.ServiceProvider.GetRequiredService<IFeedService>().ListByCategoryAsync())[0].Feeds[0];
            var subscribed = await scope.ServiceProvider.GetRequiredService<ISubscriptionService>()
                .SubscribeByEmailAsync(user!.Id, feed.Id);
            Assert.False(subscribed.IsError);
        }

        await TestSignIn.SignInAsync(client, TestAppFactory.AdminEmail, TestAppFactory.AdminPassword);
        var html = await client.GetStringAsync("/");

        Assert.Equal(1, CountOf(html, "btn-danger"));
        Assert.Equal(1, CountOf(html, ">Unsubscribe</button>"));
        // The one subscribed feed is the top of the first category, so it is visible; the rest of the visible cards
        // still offer Subscribe.
        Assert.Equal(VisibleCards - 1, CountOf(html, ">Subscribe</button>"));
        Assert.DoesNotContain("Account/Login?ReturnUrl", html);
        // Home persists the user's subscribed ids for the interactive circuit — and nothing bulky (see below).
        Assert.Contains("Blazor-Server-Component-State", html);
        Assert.InRange(PersistedStateBytes(html), 1, PersistedStateBudget);
    }

    /// <summary>
    /// Persisted component state is embedded in the page and sent back to the server inside the circuit-start
    /// message, which SignalR rejects above 32 KB — after which the circuit is dead and nothing on the page is
    /// interactive. Persisting the whole catalogue produced ~87 KB and did exactly that. Keep the payload small.
    /// </summary>
    private const int PersistedStateBudget = 8 * 1024;

    [Fact]
    public async Task Home_PersistedState_StaysFarBelowTheCircuitMessageLimit()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.InRange(PersistedStateBytes(html), 0, PersistedStateBudget);
    }

    private static int PersistedStateBytes(string html)
    {
        const string start = "<!--Blazor-Server-Component-State:";
        var from = html.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
            return 0;
        var to = html.IndexOf("-->", from, StringComparison.Ordinal);
        return to - from - start.Length;
    }

    private static int CountOf(string html, string fragment) => html.Split(fragment).Length - 1;

    [Theory]
    [InlineData("/counter")]
    [InlineData("/weather")]
    [InlineData("/auth")]
    public async Task TemplateSamplePages_AreGone(string route)
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
