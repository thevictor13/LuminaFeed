using ErrorOr;

namespace LuminaFeed.Services.Categories;

/// <summary>A category as listed in the admin area, with how many feeds it holds.</summary>
public sealed record CategorySummary(Guid Id, string Name, string? Description, int FeedCount);

/// <summary>Admin management of feed categories. Minimal create/list for now; edit/delete arrive with A1.</summary>
public interface ICategoryService
{
    /// <summary>All categories, ordered by name.</summary>
    Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a category; fails with validation errors or a conflict on a duplicate name.</summary>
    Task<ErrorOr<CategorySummary>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
}
