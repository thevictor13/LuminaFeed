using LuminaFeed.Components.Shared;
using LuminaFeed.Services.Feeds;

namespace LuminaFeed.Tests;

/// <summary>The four in-memory orderings behind the per-category order control (B2), incl. tie-breaks and case.</summary>
public sealed class FeedOrderingTests
{
    private static FeedSummary Feed(string name, int popularity) =>
        new(Guid.CreateVersion7(), name, Guid.Empty, "Technology",
            "https://feeds.test/x.xml", "https://site.test", null, null, popularity);

    // "Apple" and "banana" tie on popularity, so name (case-insensitive) breaks the tie.
    private static readonly IReadOnlyList<FeedSummary> Feeds =
        [Feed("banana", 10), Feed("Apple", 10), Feed("cherry", 50), Feed("date", 1)];

    private static IEnumerable<string> Names(FeedOrder order) => FeedOrdering.Sort(Feeds, order).Select(f => f.Name);

    [Fact]
    public void PopularityDesc_HighestFirst_NameBreaksTiesCaseInsensitive() =>
        Assert.Equal(["cherry", "Apple", "banana", "date"], Names(FeedOrder.PopularityDesc));

    [Fact]
    public void PopularityAsc_LowestFirst_NameBreaksTies() =>
        Assert.Equal(["date", "Apple", "banana", "cherry"], Names(FeedOrder.PopularityAsc));

    [Fact]
    public void NameAsc_IsCaseInsensitiveAlphabetical() =>
        Assert.Equal(["Apple", "banana", "cherry", "date"], Names(FeedOrder.NameAsc));

    [Fact]
    public void NameDesc_IsReverseCaseInsensitiveAlphabetical() =>
        Assert.Equal(["date", "cherry", "banana", "Apple"], Names(FeedOrder.NameDesc));
}
