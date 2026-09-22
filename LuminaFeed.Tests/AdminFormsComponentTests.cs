using Bunit;
using LuminaFeed.Components.Admin;
using LuminaFeed.Domain;
using LuminaFeed.Services.Categories;
using LuminaFeed.Services.Feeds;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaFeed.Tests;

/// <summary>
/// The admin add-forms, submitted through bUnit against the <b>real services</b> on in-memory SQLite: the success
/// alert and refreshed table, the form reset, and the conflict / validation alerts the page tests can't reach.
/// (Authorization is a routing concern — <see cref="AdminPagesTests"/> covers the policy; here the pages render directly.)
/// </summary>
public sealed class AdminFormsComponentTests : BunitContext
{
    private readonly SqliteTestDatabase _db = new();

    public AdminFormsComponentTests()
    {
        Services.AddSingleton<ICategoryService>(
            new CategoryService(_db, new CreateCategoryRequestValidator(), new UpdateCategoryRequestValidator()));
        Services.AddSingleton<IFeedService>(
            new FeedService(_db, new CreateFeedRequestValidator(), new UpdateFeedRequestValidator()));
    }

    /// <summary>
    /// Renders a page as an interactive server renderer would (they declare <c>@rendermode InteractiveServer</c>).
    /// Setting the renderer info locks bUnit's service container, so it happens last.
    /// </summary>
    private IRenderedComponent<TPage> RenderPage<TPage>() where TPage : IComponent
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return Render<TPage>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _db.Dispose();
    }

    [Fact]
    public async Task Categories_AddingOne_ShowsSuccess_ListsIt_AndClearsTheForm()
    {
        var cut = RenderPage<Categories>();
        cut.WaitForAssertion(() => Assert.Contains("No categories yet.", cut.Markup));

        cut.Find("#category-name").Change("Science");
        cut.Find("#category-description").Change("Research news");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("Category \"Science\" was added.", cut.Find(".alert-success").TextContent));
        var row = cut.Find("tbody tr");
        Assert.Contains("Science", row.TextContent);
        Assert.Contains("Research news", row.TextContent);
        Assert.Equal("", cut.Find("#category-name").GetAttribute("value"));
        using var ctx = _db.CreateDbContext();
        Assert.Equal("Science", Assert.Single(ctx.Categories).Name);
    }

    [Fact]
    public async Task Categories_DuplicateName_ShowsTheConflict()
    {
        _db.AddCategory("Science");
        var cut = RenderPage<Categories>();
        cut.WaitForElement("tbody tr");

        cut.Find("#category-name").Change("science");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("already exists", cut.Find(".alert-danger").TextContent));
        Assert.Empty(cut.FindAll(".alert-success"));
    }

    [Fact]
    public async Task Categories_BlankName_ShowsTheValidationMessage()
    {
        var cut = RenderPage<Categories>();
        cut.WaitForAssertion(() => Assert.Contains("No categories yet.", cut.Markup));

        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("'Name' must not be empty.", cut.Find(".alert-danger").TextContent));
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Categories);
    }

    [Fact]
    public async Task Categories_EditingOne_UpdatesTheRow_AndClosesTheDialog()
    {
        _db.AddCategory("Scince");
        var cut = RenderPage<Categories>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Edit Scince']").ClickAsync(new MouseEventArgs());

        var name = cut.WaitForElement("#edit-category-name");
        Assert.Equal("Scince", name.GetAttribute("value"));
        name.Change("Science");
        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("Category \"Science\" was updated.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        Assert.Contains("Science", cut.Find("tbody tr").TextContent);
        using var ctx = _db.CreateDbContext();
        Assert.Equal("Science", Assert.Single(ctx.Categories).Name);
    }

    [Fact]
    public async Task Categories_EditingOntoAnExistingName_ShowsTheConflictInTheDialog()
    {
        _db.AddCategory("World News");
        _db.AddCategory("Markets");
        var cut = RenderPage<Categories>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Edit Markets']").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#edit-category-name").Change("World News");
        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("already exists", cut.Find(".modal .alert-danger").TextContent));
        Assert.NotEmpty(cut.FindAll(".modal"));
        Assert.Empty(cut.FindAll(".alert-success"));
    }

    [Fact]
    public async Task Categories_DeletingAnEmptyCategory_RemovesTheRow()
    {
        _db.AddCategory("Science");
        var cut = RenderPage<Categories>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Delete Science']").ClickAsync(new MouseEventArgs());
        await cut.Find(".modal .btn-danger").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("Category \"Science\" was deleted.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        Assert.Contains("No categories yet.", cut.Markup);
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Categories);
    }

    [Fact]
    public void Categories_DeleteButton_IsDisabledForACategoryWithFeeds()
    {
        var science = _db.AddCategory("Science");
        _db.AddFeed(science.Id, "Nature");
        var cut = RenderPage<Categories>();
        cut.WaitForElement("tbody tr");

        Assert.True(cut.Find("button[aria-label='Delete Science']").HasAttribute("disabled"));
    }

    [Fact]
    public void Feeds_WithoutCategories_PointsAtTheCategoriesPage()
    {
        var cut = RenderPage<Feeds>();

        cut.WaitForAssertion(() => Assert.Contains("add a category", cut.Find("a[href='admin/categories']").TextContent));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public async Task Feeds_AddingOne_ShowsSuccess_ListsIt_AndKeepsTheCategorySelected()
    {
        var category = _db.AddCategory("Science");
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("#feed-category");

        cut.Find("#feed-category").Change(category.Id.ToString());
        cut.Find("#feed-name").Change("Nature");
        cut.Find("#feed-url").Change("https://www.nature.com/nature.rss");
        cut.Find("#feed-site-url").Change("https://www.nature.com");
        cut.Find("#feed-popularity").Change("42");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("Feed \"Nature\" was added.", cut.Find(".alert-success").TextContent));
        var row = cut.Find("tbody tr");
        Assert.Contains("Science", row.TextContent);
        Assert.Contains("Nature", row.TextContent);
        Assert.Contains("42", row.TextContent);
        // The per-feed fields reset, but the category stays selected for the next feed.
        Assert.Equal("", cut.Find("#feed-name").GetAttribute("value"));
        Assert.Equal(category.Id.ToString(), cut.Find("#feed-category").GetAttribute("value"));
        using var ctx = _db.CreateDbContext();
        var feed = Assert.Single(ctx.Feeds);
        Assert.Equal(category.Id, feed.CategoryId);
        Assert.Equal(42, feed.Popularity);
    }

    [Fact]
    public async Task Feeds_EmptySubmit_ShowsEveryValidationMessage()
    {
        _db.AddCategory("Science");
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("#feed-category");

        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".alert-danger").TextContent;
            Assert.Contains("'Name' must not be empty.", alert);
            Assert.Contains("A category must be selected.", alert);
            Assert.Contains("'Feed Url' must not be empty.", alert);
            Assert.Contains("'Site Url' must not be empty.", alert);
        });
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Feeds);
    }

    [Fact]
    public async Task Feeds_NonHttpUrl_IsRejectedWithTheUrlMessage()
    {
        var category = _db.AddCategory("Science");
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("#feed-category");

        cut.Find("#feed-category").Change(category.Id.ToString());
        cut.Find("#feed-name").Change("Nope");
        cut.Find("#feed-url").Change("javascript:alert(1)");
        cut.Find("#feed-site-url").Change("https://www.example.test");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("'Feed Url' must be an absolute http(s) URL.", cut.Find(".alert-danger").TextContent));
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Feeds);
    }

    [Fact]
    public async Task Feeds_EditingOne_UpdatesTheRow_WithTheCategoryPreselected()
    {
        var science = _db.AddCategory("Science");
        _db.AddFeed(science.Id, "Natrue", "https://nature.test/rss.xml");
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Edit Natrue']").ClickAsync(new MouseEventArgs());

        var name = cut.WaitForElement("#edit-feed-name");
        Assert.Equal("Natrue", name.GetAttribute("value"));
        Assert.Equal(science.Id.ToString(), cut.Find("#edit-feed-category").GetAttribute("value"));
        name.Change("Nature");
        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("Feed \"Nature\" was updated.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        Assert.Contains("Nature", cut.Find("tbody tr").TextContent);
        using var ctx = _db.CreateDbContext();
        Assert.Equal("Nature", Assert.Single(ctx.Feeds).Name);
    }

    [Fact]
    public async Task Feeds_EditingOntoAnotherFeedsUrl_ShowsTheConflictInTheDialog()
    {
        var science = _db.AddCategory("Science");
        _db.AddFeed(science.Id, "BBC", "https://bbc.test/rss.xml");
        _db.AddFeed(science.Id, "Reuters", "https://reuters.test/rss.xml");
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Edit Reuters']").ClickAsync(new MouseEventArgs());
        cut.WaitForElement("#edit-feed-url").Change("https://bbc.test/rss.xml");
        await cut.Find(".modal form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains("already exists", cut.Find(".modal .alert-danger").TextContent));
        Assert.NotEmpty(cut.FindAll(".modal"));
        Assert.Empty(cut.FindAll(".alert-success"));
    }

    [Fact]
    public async Task Feeds_DeletingOne_ShowsTheImpact_AndRemovesTheRow()
    {
        var science = _db.AddCategory("Science");
        var feed = _db.AddFeed(science.Id, "Nature", "https://nature.test/rss.xml");
        var user = _db.AddUser();
        using (var seed = _db.CreateDbContext())
        {
            seed.Subscriptions.Add(new Subscription { UserId = user.Id, FeedId = feed.Id, EmailEnabled = true });
            seed.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a1", Title = "t1", Link = "https://nature.test/1" });
            seed.Articles.Add(new Article { FeedId = feed.Id, ExternalId = "a2", Title = "t2", Link = "https://nature.test/2" });
            seed.SaveChanges();
        }
        var cut = RenderPage<Feeds>();
        cut.WaitForElement("tbody tr");

        await cut.Find("button[aria-label='Delete Nature']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find(".modal-body").TextContent;
            Assert.Contains("1 subscription", body);
            Assert.Contains("2 stored articles", body);
        });
        await cut.Find(".modal .btn-danger").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("Feed \"Nature\" was deleted.", cut.Find(".alert-success").TextContent));
        Assert.Empty(cut.FindAll(".modal"));
        Assert.Contains("No feeds yet.", cut.Markup);
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Feeds);
        Assert.Empty(ctx.Subscriptions);
        Assert.Empty(ctx.Articles);
    }
}
