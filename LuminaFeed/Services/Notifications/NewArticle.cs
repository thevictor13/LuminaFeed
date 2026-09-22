namespace LuminaFeed.Services.Notifications;

/// <summary>
/// A newly stored article as polling hands it to the notification services: a plain value, not the persisted
/// entity, so no channel depends on EF navigations or change tracking.
/// </summary>
/// <param name="ArticleId">The stored <c>Article</c> row, for channels that need to refer back to it.</param>
/// <param name="FeedId">The feed it belongs to. Digests group by this — feed names are not unique.</param>
/// <param name="Link">Absolute http(s) URL (the parser lets nothing else through).</param>
/// <param name="Summary">Plain text, already stripped of markup by the parser; null when the feed gives none.</param>
public sealed record NewArticle(
    Guid ArticleId,
    Guid FeedId,
    string FeedName,
    string Title,
    string Link,
    string? Summary,
    string? ImageUrl,
    DateTimeOffset? PublishedAt);
