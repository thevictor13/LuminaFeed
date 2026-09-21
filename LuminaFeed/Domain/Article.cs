namespace LuminaFeed.Domain;

/// <summary>An item fetched from a <see cref="Feed"/> during polling. De-duplicated per feed by
/// <see cref="ExternalId"/> (the RSS &lt;guid&gt; or, failing that, the item link).</summary>
public sealed class Article : EntityBase
{
    public Guid FeedId { get; set; }
    public Feed Feed { get; set; } = null!;

    /// <summary>Stable per-feed identity from the source (RSS &lt;guid&gt; or link) used for de-dup.</summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }

    /// <summary>The article URL.</summary>
    public required string Link { get; set; }

    /// <summary>First paragraph(s); null when the feed provides none.</summary>
    public string? Summary { get; set; }

    /// <summary>Article image (RSS image or scraped og:image); null when none is available.</summary>
    public string? ImageUrl { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}
