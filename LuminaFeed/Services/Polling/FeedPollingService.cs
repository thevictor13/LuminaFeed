using LuminaFeed.Data;
using LuminaFeed.Domain;
using LuminaFeed.Options;
using LuminaFeed.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuminaFeed.Services.Polling;

public sealed class FeedPollingService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IFeedFetcher fetcher,
    IOptions<PollingOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedPollingService> logger) : IFeedPollingService
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<NewArticle>>> PollAsync(
        CancellationToken cancellationToken = default)
    {
        List<Guid> activeFeedIds;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            // "Active" = at least one user is subscribed; nobody is waiting on the rest of the catalogue.
            activeFeedIds = await db.Feeds
                .Where(f => f.Subscriptions.Any())
                .OrderBy(f => f.Name)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);
        }

        var byEmail = new Dictionary<string, List<NewArticle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var feedId in activeFeedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await PollFeedAsync(feedId, byEmail, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken feed must not cost the others their poll.
                logger.LogError(ex, "Polling feed {FeedId} failed.", feedId);
            }
        }

        return byEmail.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<NewArticle>)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private async Task PollFeedAsync(Guid feedId, Dictionary<string, List<NewArticle>> byEmail, CancellationToken cancellationToken)
    {
        // A context per feed keeps a failed save from poisoning the next feed's unit of work.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var feed = await db.Feeds.SingleOrDefaultAsync(f => f.Id == feedId, cancellationToken);
        if (feed is null)
            return;

        var fetched = await fetcher.FetchAsync(feed.FeedUrl, cancellationToken);
        if (fetched.IsError)
        {
            logger.LogWarning("Skipping feed '{Feed}': {Reason}", feed.Name, fetched.FirstError.Description);
            return;
        }

        var parsed = FeedParser.Parse(fetched.Value, Uri.TryCreate(feed.FeedUrl, UriKind.Absolute, out var baseUri) ? baseUri : null);
        if (parsed.IsError)
        {
            logger.LogWarning("Skipping feed '{Feed}': {Reason}", feed.Name, parsed.FirstError.Description);
            return;
        }

        var candidates = parsed.Value
            .Select(item => ToArticle(feed, item))
            .OfType<Article>()
            .DistinctBy(a => a.ExternalId)
            .ToList();

        var candidateIds = candidates.Select(a => a.ExternalId).ToList();
        var knownIds = await db.Articles
            .Where(a => a.FeedId == feedId && candidateIds.Contains(a.ExternalId))
            .Select(a => a.ExternalId)
            .ToHashSetAsync(cancellationToken);
        var newArticles = candidates.Where(a => !knownIds.Contains(a.ExternalId)).ToList();

        // Only a successful poll counts: a feed that has never been read successfully keeps its "first poll"
        // status, so its eventual backlog is still capped below.
        var isFirstPoll = feed.LastPolledAt is null;
        feed.LastPolledAt = timeProvider.GetUtcNow();
        db.Articles.AddRange(newArticles);
        await db.SaveChangesAsync(cancellationToken);

        // Debug: this fires for every active feed every interval; the pass summary is logged by the loop.
        logger.LogDebug(
            "Polled '{Feed}': {NewCount} new of {ItemCount} items.", feed.Name, newArticles.Count, candidates.Count);

        // Newest first; undated items keep their document order after the dated ones.
        IEnumerable<Article> ordered = newArticles
            .OrderByDescending(a => a.PublishedAt.HasValue)
            .ThenByDescending(a => a.PublishedAt);
        var notifiable = (isFirstPoll ? ordered.Take(options.Value.FirstPollNotificationCap) : ordered)
            .Select(a => ToNewArticle(feed, a))
            .ToList();
        if (notifiable.Count == 0)
            return;

        var emails = await db.Subscriptions
            .Where(s => s.FeedId == feedId && s.EmailEnabled && s.User.EmailConfirmed && s.User.Email != null)
            .Select(s => s.User.Email!)
            .ToListAsync(cancellationToken);

        foreach (var email in emails)
        {
            if (!byEmail.TryGetValue(email, out var articles))
                byEmail[email] = articles = [];
            articles.AddRange(notifiable);
        }
    }

    /// <summary>Maps a parsed item onto the entity, fitting the column limits; null when it can't be stored faithfully.</summary>
    private static Article? ToArticle(Feed feed, ParsedFeedItem item)
    {
        // A truncated URL is a broken URL, so an over-long link drops the item (and an over-long image, the image).
        if (item.Link.Length > Article.UrlMaxLength)
            return null;

        return new Article
        {
            FeedId = feed.Id,
            ExternalId = Truncate(item.ExternalId, Article.ExternalIdMaxLength),
            Title = Truncate(item.Title, Article.TitleMaxLength),
            Link = item.Link,
            Summary = item.Summary,
            ImageUrl = item.ImageUrl is { Length: <= Article.UrlMaxLength } ? item.ImageUrl : null,
            PublishedAt = item.PublishedAt,
        };
    }

    /// <summary>The stored article as the notification services see it: a value with the feed's name attached.</summary>
    private static NewArticle ToNewArticle(Feed feed, Article article) =>
        new(article.Id, feed.Id, feed.Name, article.Title, article.Link, article.Summary, article.ImageUrl, article.PublishedAt);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
