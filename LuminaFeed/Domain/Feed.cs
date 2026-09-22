namespace LuminaFeed.Domain;

/// <summary>An admin-curated RSS/Atom feed belonging to a <see cref="Category"/>.</summary>
public sealed class Feed : EntityBase
{
    // Column limits: the single source for the EF configuration, the request validators and the admin forms.
    public const int NameMaxLength = 200;
    public const int UrlMaxLength = 2048;
    public const int DescriptionMaxLength = 1000;
    public const int ETagMaxLength = 512;
    public const int LastModifiedMaxLength = 256;

    public required string Name { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>The RSS/Atom endpoint that is polled.</summary>
    public required string FeedUrl { get; set; }

    /// <summary>The human-facing site the feed belongs to.</summary>
    public required string SiteUrl { get; set; }

    /// <summary>Feed image (RSS image or scraped og:image); null when none is available.</summary>
    public string? ImageUrl { get; set; }

    public string? Description { get; set; }

    /// <summary>Fixed popularity figure (from research) driving the public list's default ordering.</summary>
    public int Popularity { get; set; }

    // --- Polling cache (populated by the polling service, C4) ---

    /// <summary>Last seen HTTP ETag, sent as If-None-Match on the next poll.</summary>
    public string? ETag { get; set; }

    /// <summary>Last seen HTTP Last-Modified (raw header value), sent as If-Modified-Since.</summary>
    public string? LastModified { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];
    public ICollection<Article> Articles { get; set; } = [];
}
