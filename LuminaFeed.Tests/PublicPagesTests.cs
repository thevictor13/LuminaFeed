using System.Net;
using LuminaFeed.Services.Feeds;
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
