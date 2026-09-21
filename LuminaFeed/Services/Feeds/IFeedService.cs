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

/// <summary>The admin-curated feed catalogue. Minimal create/list for now; edit/delete arrive with A2.</summary>
public interface IFeedService
{
    /// <summary>All feeds, ordered by category name then feed name (admin list).</summary>
    Task<IReadOnlyList<FeedSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a feed; fails with validation errors, not-found for an unknown category, or a conflict on a
    /// duplicate feed URL.
    /// </summary>
    Task<ErrorOr<FeedSummary>> CreateAsync(CreateFeedRequest request, CancellationToken cancellationToken = default);
}
