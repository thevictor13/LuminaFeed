using LuminaFeed.Services.Feeds;

namespace LuminaFeed.Components.Shared;

/// <summary>The order options offered by the public list's per-category order control (B2).</summary>
public enum FeedOrder
{
    /// <summary>Most popular first — the fixed default (matches <see cref="IFeedService.ListByCategoryAsync"/>).</summary>
    PopularityDesc,
    PopularityAsc,
    NameAsc,
    NameDesc,
}

/// <summary>
/// In-memory ordering for the per-category order control. The whole catalogue is already in the circuit's memory
/// (see <c>Home.razor</c>), and <see cref="FeedSummary"/> carries both <c>Popularity</c> and <c>Name</c>, so every
/// mode is a pure re-sort of the already-loaded list — no service query.
/// </summary>
public static class FeedOrdering
{
    public static IReadOnlyList<FeedSummary> Sort(IReadOnlyList<FeedSummary> feeds, FeedOrder order) => order switch
    {
        // Popularity ties break by name, matching the service's default ordering.
        FeedOrder.PopularityDesc => [.. feeds.OrderByDescending(f => f.Popularity).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)],
        FeedOrder.PopularityAsc => [.. feeds.OrderBy(f => f.Popularity).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)],
        FeedOrder.NameAsc => [.. feeds.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)],
        FeedOrder.NameDesc => [.. feeds.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)],
        _ => feeds,
    };

    /// <summary>The human label shown for each option in the order menu.</summary>
    public static string Label(FeedOrder order) => order switch
    {
        FeedOrder.PopularityDesc => "Most popular",
        FeedOrder.PopularityAsc => "Least popular",
        FeedOrder.NameAsc => "Name (A–Z)",
        FeedOrder.NameDesc => "Name (Z–A)",
        _ => order.ToString(),
    };
}
