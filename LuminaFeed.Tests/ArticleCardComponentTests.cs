using Bunit;
using LuminaFeed.Components.Shared;
using LuminaFeed.Services.Feeds;

namespace LuminaFeed.Tests;

/// <summary>
/// The B4 article card rendered with bUnit. It takes a plain <see cref="ArticleSummary"/>, so no database or DI is
/// needed. Every display field is subject to availability, so the tests cover both the present and the absent case.
/// </summary>
public sealed class ArticleCardComponentTests : BunitContext
{
    private static readonly DateTimeOffset Published = new(2026, 3, 4, 8, 0, 0, TimeSpan.Zero);

    private IRenderedComponent<ArticleCard> RenderCard(ArticleSummary article) =>
        Render<ArticleCard>(ps => ps.Add(p => p.Article, article));

    [Fact]
    public void RendersTitleLink_Summary_Image_AndDate_WhenPresent()
    {
        var cut = RenderCard(new ArticleSummary(
            Guid.CreateVersion7(), "Big headline", "https://news.test/story",
            "The first paragraph of the story.", "https://img.test/story.png", Published));

        var link = cut.Find(".article-card h2 a");
        Assert.Equal("Big headline", link.TextContent.Trim());
        Assert.Equal("https://news.test/story", link.GetAttribute("href"));
        // Outbound link to an untrusted target opens safely.
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("_blank", link.GetAttribute("target"));

        Assert.Contains("The first paragraph", cut.Find(".article-card .card-text").TextContent);
        Assert.Equal("https://img.test/story.png", cut.Find("img.article-card-image").GetAttribute("src"));
        Assert.NotEmpty(cut.FindAll("time"));
    }

    [Fact]
    public void OmitsImage_Summary_AndDate_WhenAbsent()
    {
        var cut = RenderCard(new ArticleSummary(
            Guid.CreateVersion7(), "Just a title", "https://news.test/bare", null, null, null));

        Assert.Empty(cut.FindAll("img.article-card-image"));
        Assert.Empty(cut.FindAll(".article-card .card-text"));
        Assert.Empty(cut.FindAll("time"));
        // The title link is always present.
        Assert.Equal("Just a title", cut.Find(".article-card h2 a").TextContent.Trim());
    }

    [Fact]
    public void EncodesUntrustedTitleAndSummary_SoInjectedMarkupIsInert()
    {
        // Title and summary come from an untrusted feed. Blazor auto-encodes @expr (the card must never use
        // MarkupString), so an injected <script> is rendered as text, not as a live element.
        var cut = RenderCard(new ArticleSummary(
            Guid.CreateVersion7(), "<script>alert(1)</script>", "https://news.test/x",
            "Tom & Jerry < Best", null, Published));

        // No live <script> element was created; the title is inert text.
        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("<script>alert(1)</script>", cut.Find(".article-card h2 a").TextContent);
        // The raw markup carries the HTML-encoded forms, never the live tag or a bare ampersand.
        Assert.Contains("&lt;script&gt;", cut.Markup);
        Assert.DoesNotContain("<script>", cut.Markup);
        Assert.Contains("Tom &amp; Jerry &lt; Best", cut.Markup);
    }
}
