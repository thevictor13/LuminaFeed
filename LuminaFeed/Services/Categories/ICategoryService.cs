using ErrorOr;

namespace LuminaFeed.Services.Categories;

/// <summary>A category as listed in the admin area, with how many feeds it holds.</summary>
public sealed record CategorySummary(Guid Id, string Name, string? Description, int FeedCount);

/// <summary>Admin management of feed categories: list, create, edit and delete (A1).</summary>
public interface ICategoryService
{
    /// <summary>All categories, ordered by name.</summary>
    Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a category; fails with validation errors or a conflict on a duplicate name.</summary>
    Task<ErrorOr<CategorySummary>> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames / re-describes a category; fails with validation errors, not-found for an unknown id, or a conflict
    /// on a duplicate name (ignoring the category being edited).
    /// </summary>
    Task<ErrorOr<CategorySummary>> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a category; not-found for an unknown id, or a conflict while it still holds feeds.</summary>
    Task<ErrorOr<Deleted>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
