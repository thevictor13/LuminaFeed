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

    // --- B2: the per-category order control -------------------------------------------------------

    // Popularity order (default) and name order differ, so we can tell which is applied.
    private static CategoryFeeds Ordered() => new(Guid.CreateVersion7(), "Technology",
        [Feed("Zebra Times", popularity: 100), Feed("Alpha News", popularity: 50), Feed("Mango Post", popularity: 10)]);

    private const string OrderButton = "button[aria-label='Order Technology by']";

    private static IReadOnlyList<string> Titles(IRenderedComponent<CategorySection> cut) =>
        [.. cut.FindAll(".feed-card h3.card-title").Select(e => e.TextContent.Trim())];

    [Fact]
    public void DefaultOrder_IsMostPopularFirst()
    {
        var cut = RenderSection(Ordered());

        Assert.Equal(["Zebra Times", "Alpha News", "Mango Post"], Titles(cut));
        Assert.Empty(cut.FindAll(".dropdown-menu.show")); // menu starts closed
    }

    [Fact]
    public async Task OpeningTheOrderMenu_ShowsTheFourOptions()
    {
        var cut = RenderSection(Ordered());

        await cut.Find(OrderButton).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".dropdown-menu.show .dropdown-item").Count));
    }

    [Fact]
    public async Task SelectingNameAscending_ReordersTheCards_ClosesTheMenu_AndMarksTheActiveOption()
    {
        var cut = RenderSection(Ordered());
        await cut.Find(OrderButton).ClickAsync(new MouseEventArgs());
        var nameAsc = cut.FindAll(".dropdown-menu.show .dropdown-item").Single(b => b.TextContent.Contains("A–Z"));

        await nameAsc.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Alpha News", "Mango Post", "Zebra Times"], Titles(cut));
            Assert.Empty(cut.FindAll(".dropdown-menu.show")); // selecting closes the menu
        });

        // Reopen: the chosen option is marked active.
        await cut.Find(OrderButton).ClickAsync(new MouseEventArgs());
        var active = cut.Find(".dropdown-menu.show .dropdown-item.active");
        Assert.Contains("A–Z", active.TextContent);
    }

    [Fact]
    public async Task ClickingTheBackdrop_ClosesTheMenu()
    {
        var cut = RenderSection(Ordered());
        await cut.Find(OrderButton).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dropdown-menu.show")));

        await cut.Find(".order-backdrop").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dropdown-menu.show")));
    }
}
