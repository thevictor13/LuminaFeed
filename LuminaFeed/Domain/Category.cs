namespace LuminaFeed.Domain;

/// <summary>An admin-curated grouping of feeds (e.g. World News, Technology). Seeded from research (G0.7).</summary>
public sealed class Category : EntityBase
{
    // Column limits: the single source for the EF configuration, the request validators and the admin forms.
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ICollection<Feed> Feeds { get; set; } = [];
}
