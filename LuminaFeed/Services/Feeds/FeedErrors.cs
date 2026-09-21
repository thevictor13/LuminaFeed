using ErrorOr;

namespace LuminaFeed.Services.Feeds;

public static class FeedErrors
{
    public static Error DuplicateFeedUrl(string feedUrl) =>
        Error.Conflict("Feed.DuplicateFeedUrl", $"A feed with the URL '{feedUrl}' already exists.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("Feed.NotFound", $"Feed '{id}' was not found.");
}
