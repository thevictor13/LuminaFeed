using Bunit;
using LuminaFeed.Components.Shared;
using Microsoft.AspNetCore.Components.Web;

namespace LuminaFeed.Tests;

/// <summary>
/// The B3 category filter dropdown rendered with bUnit. Like <c>OrderMenu</c>, it drives its own open/close in the
/// circuit (no Bootstrap JS) and owns no data — the parent supplies the options and the selection — so these tests
/// need no database or DI.
/// </summary>
public sealed class CategoryFilterComponentTests : BunitContext
{
    private static readonly (Guid Id, string Name) News = (Guid.CreateVersion7(), "World News");
    private static readonly (Guid Id, string Name) Tech = (Guid.CreateVersion7(), "Technology");
    private static readonly IReadOnlyList<(Guid Id, string Name)> Options = [News, Tech];

    private const string Trigger = "button[aria-label='Filter feeds by category']";

    private IRenderedComponent<CategoryFilter> RenderFilter(Guid? selected, Action<Guid?>? onChange = null) =>
        Render<CategoryFilter>(ps => ps
            .Add(p => p.Options, Options)
            .Add(p => p.Selected, selected)
            .Add(p => p.OnChange, onChange ?? (_ => { })));

    [Fact]
    public void Default_ShowsAllFeeds_AndStartsClosed()
    {
        var cut = RenderFilter(selected: null);

        Assert.Contains("Category: All", cut.Find(Trigger).TextContent);
        Assert.Empty(cut.FindAll(".dropdown-menu.show"));
    }

    [Fact]
    public async Task Opening_ListsAllFeedsPlusEveryCategory()
    {
        var cut = RenderFilter(selected: null);

        await cut.Find(Trigger).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".dropdown-menu.show .dropdown-item").Select(e => e.TextContent.Trim()).ToList();
            Assert.Equal(["All feeds", "World News", "Technology"], items);
        });
    }

    [Fact]
    public async Task SelectingACategory_RaisesOnChange_WithItsId_AndClosesTheMenu()
    {
        Guid? chosen = null;
        var raised = 0;
        var cut = RenderFilter(selected: null, onChange: id => { chosen = id; raised++; });

        await cut.Find(Trigger).ClickAsync(new MouseEventArgs());
        var tech = cut.FindAll(".dropdown-menu.show .dropdown-item").Single(b => b.TextContent.Contains("Technology"));
        await tech.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, raised);
            Assert.Equal(Tech.Id, chosen);
            Assert.Empty(cut.FindAll(".dropdown-menu.show"));
        });
    }

    [Fact]
    public async Task SelectedCategory_IsShownOnTheTrigger_AndMarkedActive()
    {
        var cut = RenderFilter(selected: Tech.Id);

        Assert.Contains("Category: Technology", cut.Find(Trigger).TextContent);

        await cut.Find(Trigger).ClickAsync(new MouseEventArgs());
        var active = cut.Find(".dropdown-menu.show .dropdown-item.active");
        Assert.Contains("Technology", active.TextContent);
    }

    [Fact]
    public async Task ClickingTheBackdrop_ClosesTheMenu()
    {
        var cut = RenderFilter(selected: null);
        await cut.Find(Trigger).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dropdown-menu.show")));

        await cut.Find(".category-filter-backdrop").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dropdown-menu.show")));
    }
}
