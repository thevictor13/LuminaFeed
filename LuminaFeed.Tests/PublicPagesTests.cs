using System.Net;
using LuminaFeed.Data;
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
        Assert.Contains("feed-card", html);
        // Every seeded feed gets a card.
        Assert.Equal(115, html.Split("class=\"card h-100 feed-card\"").Length - 1);
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
            var created = await feeds.CreateAsync(new CreateFeedRequest(
                "Skeleton Gazette", category.CategoryId, "https://gazette.example.test/rss.xml",
                "https://gazette.example.test", ImageUrl: "https://gazette.example.test/logo.png"));
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

        // Anonymous users are treated as having no subscriptions: every card offers Subscribe, as a
        // link to the login page carrying the location to return to.
        Assert.Equal(115, CountOf(html, ">Subscribe</a>"));
        Assert.Contains("href=\"Account/Login?ReturnUrl=%2F\"", html);
        Assert.DoesNotContain("Unsubscribe", html);
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
        Assert.Equal(114, CountOf(html, ">Subscribe</button>"));
        Assert.DoesNotContain("Account/Login?ReturnUrl", html);
        // The prerendered catalogue is handed to the interactive circuit instead of being re-queried.
        Assert.Contains("Blazor-Server-Component-State", html);
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
