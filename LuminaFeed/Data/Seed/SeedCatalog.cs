namespace LuminaFeed.Data.Seed;

/// <summary>
/// Shape of the G0.R research file (<c>doc/research/rss-feeds.json</c>), embedded as a resource and
/// deserialized by <see cref="SeedCatalogLoader"/>. Only the fields the seeder needs are modelled;
/// unmodelled JSON members (e.g. <c>generatedOn</c>) are ignored.
/// </summary>
internal sealed record SeedCatalog
{
    public IReadOnlyList<SeedCategory> Categories { get; init; } = [];
    public IReadOnlyList<SeedFeed> Feeds { get; init; } = [];
}

internal sealed record SeedCategory
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

internal sealed record SeedFeed
{
    public required string Name { get; init; }

    /// <summary>Name of the owning category; matches a <see cref="SeedCategory.Name"/>.</summary>
    public required string Category { get; init; }

    public required string FeedUrl { get; init; }
    public required string SiteUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? Description { get; init; }
    public int Popularity { get; init; }
}
