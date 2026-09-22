using Bunit;
using LuminaFeed.Components.Shared;
using LuminaFeed.Services.Feeds;
using Microsoft.AspNetCore.Components.Web;

namespace LuminaFeed.Tests;

/// <summary>
/// The per-category paging (B1: cap at five with a "more" button) rendered with bUnit. The component takes its
/// feeds as a plain <see cref="CategoryFeeds"/> value, so these tests need no database or DI — the cap and the
/// "more" step are pure client-side slicing over the list already in the circuit's memory.
/// </summary>
public sealed class CategorySectionComponentTests : BunitContext
{
    private static FeedSummary Feed(string name, int popularity = 0) =>
        new(Guid.CreateVersion7(), name, Guid.Empty, "Technology",
            $"https://feeds.test/{Guid.NewGuid():N}.xml", "https://site.test", null, null, popularity);

    private static CategoryFeeds WithFeeds(string name, int count) =>
        new(Guid.CreateVersion7(), name, [.. Enumerable.Range(1, count).Select(i => Feed($"Feed {i:D2}", popularity: i))]);

    private IRenderedComponent<CategorySection> RenderSection(CategoryFeeds category) =>
        Render<CategorySection>(ps => ps
            .Add(p => p.Category, category)
            .Add(p => p.LoginUrl, "Account/Login"));

    private static string MoreButton(string category) => $"button[aria-label='Show more {category} feeds']";

    [Fact]
    public void ShowsAtMostFiveCards_WithAMoreButton_WhenThereAreMore()
    {
        var cut = RenderSection(WithFeeds("Technology", 7));

        Assert.Equal(5, cut.FindAll(".feed-card").Count);
        Assert.NotEmpty(cut.FindAll(MoreButton("Technology")));
    }

    [Fact]
    public async Task ClickingMore_RevealsFiveMore_AndHidesTheButtonWhenExhausted()
    {
        var cut = RenderSection(WithFeeds("Technology", 7));

        await cut.Find(MoreButton("Technology")).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(7, cut.FindAll(".feed-card").Count);
            Assert.Empty(cut.FindAll(MoreButton("Technology")));
        });
    }

    [Fact]
    public void NoMoreButton_WhenFiveOrFewerFeeds()
    {
        var cut = RenderSection(WithFeeds("Technology", 5));

        Assert.Equal(5, cut.FindAll(".feed-card").Count);
        Assert.Empty(cut.FindAll(MoreButton("Technology")));
    }

    [Fact]
    public void EachVisibleCard_CarriesAnAccessiblySubscribeControl()
    {
        var cut = RenderSection(new CategoryFeeds(Guid.CreateVersion7(), "Technology", [Feed("BBC News")]));

        // Anonymous (IsAuthenticated defaults to false): Subscribe renders as a login link named for its feed.
        Assert.NotNull(cut.Find("a[aria-label='Subscribe to BBC News']"));
    }
}
