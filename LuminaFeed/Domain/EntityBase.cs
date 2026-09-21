namespace LuminaFeed.Domain;

/// <summary>
/// Base type for all new domain entities. Ids are GUID v7 (time-ordered) generated on construction,
/// per the project convention. EF is configured to use this client-generated value verbatim.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}
