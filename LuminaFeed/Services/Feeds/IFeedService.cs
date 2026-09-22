using ErrorOr;

namespace LuminaFeed.Services.Feeds;

/// <summary>A feed as shown in lists and on cards.</summary>
public sealed record FeedSummary(
    Guid Id,
    string Name,
    Guid CategoryId,
    string CategoryName,
    string FeedUrl,
    string SiteUrl,
    string? ImageUrl,
    string? Description,
    int Popularity);

/// <summary>A category and its feeds, as grouped on the public main view.</summary>
public sealed record CategoryFeeds(Guid CategoryId, string CategoryName, IReadOnlyList<FeedSummary> Feeds);

/// <summary>What deleting a feed would also remove — shown in the delete confirmation.</summary>
public sealed record FeedDeletionImpact(Guid Id, string Name, int SubscriptionCount, int ArticleCount);

/// <summary>An article as shown on a feed's page. All display fields are subject to availability.</summary>
public sealed record ArticleSummary(
    Guid Id,
    string Title,
    string Link,
    string? Summary,
    string? ImageUrl,
    DateTimeOffset? PublishedAt);

/// <summary>A feed and its latest articles, as shown on the feed page (B4).</summary>
public sealed record FeedDetail(FeedSummary Feed, IReadOnlyList<ArticleSummary> Articles);

/// <summary>The admin-curated feed catalogue: list, create, edit and delete (A2), plus the public feed-detail read (B4).</summary>
public interface IFeedService
{
    /// <summary>All feeds, ordered by category name then feed name (admin list).</summary>
    Task<IReadOnlyList<FeedSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The public main view: categories by name, each with its feeds by popularity (highest first, then name).
    /// Categories without feeds are omitted.
    /// </summary>
    Task<IReadOnlyList<CategoryFeeds>> ListByCategoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A feed and its newest <paramref name="maxArticles"/> articles (most recent first) for the feed page (B4).
    /// Returns <c>null</c> when the feed does not exist.
    /// </summary>
    Task<FeedDetail?> GetFeedDetailAsync(Guid feedId, int maxArticles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a feed; fails with validation errors, not-found for an unknown category, or a conflict on a
    /// duplicate feed URL.
    /// </summary>
    Task<ErrorOr<FeedSummary>> CreateAsync(CreateFeedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits a feed; fails with validation errors, not-found for an unknown feed or category, or a conflict on a
    /// duplicate feed URL (ignoring the feed being edited).
    /// </summary>
    Task<ErrorOr<FeedSummary>> UpdateAsync(UpdateFeedRequest request, CancellationToken cancellationToken = default);

    /// <summary>The subscription and article counts a delete would take with the feed; not-found for an unknown id.</summary>
    Task<ErrorOr<FeedDeletionImpact>> GetDeletionImpactAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a feed and (by cascade) its subscriptions and articles; not-found for an unknown id.</summary>
    Task<ErrorOr<Deleted>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
