using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Tests;

/// <summary>
/// The entities' <c>*MaxLength</c> constants are the single source for the EF column limits, the request
/// validators and the admin forms. The validators and forms reference the constants directly, so the only drift
/// still possible is between a constant and the mapped column — which is what this guards.
/// </summary>
public sealed class LimitsTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(typeof(Category), nameof(Category.Name), Category.NameMaxLength)]
    [InlineData(typeof(Category), nameof(Category.Description), Category.DescriptionMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.Name), Feed.NameMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.FeedUrl), Feed.UrlMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.SiteUrl), Feed.UrlMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.ImageUrl), Feed.UrlMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.Description), Feed.DescriptionMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.ETag), Feed.ETagMaxLength)]
    [InlineData(typeof(Feed), nameof(Feed.LastModified), Feed.LastModifiedMaxLength)]
    [InlineData(typeof(Subscription), nameof(Subscription.UserId), Subscription.UserIdMaxLength)]
    [InlineData(typeof(Subscription), nameof(Subscription.SlackWebhookUrl), Subscription.SlackWebhookUrlMaxLength)]
    [InlineData(typeof(Article), nameof(Article.ExternalId), Article.ExternalIdMaxLength)]
    [InlineData(typeof(Article), nameof(Article.Title), Article.TitleMaxLength)]
    [InlineData(typeof(Article), nameof(Article.Link), Article.UrlMaxLength)]
    [InlineData(typeof(Article), nameof(Article.ImageUrl), Article.UrlMaxLength)]
    public void EntityConstant_MatchesTheMappedColumnLimit(Type entity, string property, int expected)
    {
        using var ctx = _db.CreateDbContext();

        var mapped = ctx.Model.FindEntityType(entity)!.FindProperty(property)!.GetMaxLength();

        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void ArticleSummary_IsDeliberatelyUnbounded()
    {
        using var ctx = _db.CreateDbContext();

        Assert.Null(ctx.Model.FindEntityType(typeof(Article))!.FindProperty(nameof(Article.Summary))!.GetMaxLength());
    }
}
