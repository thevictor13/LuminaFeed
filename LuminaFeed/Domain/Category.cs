namespace LuminaFeed.Domain;

/// <summary>An admin-curated grouping of feeds (e.g. World News, Technology). Seeded from research (G0.7).</summary>
public sealed class Category : EntityBase
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public ICollection<Feed> Feeds { get; set; } = [];
}
