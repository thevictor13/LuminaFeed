using ErrorOr;
using FluentValidation;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Categories;

public sealed class CategoryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IValidator<CreateCategoryRequest> validator) : ICategoryService
{
    public async Task<IReadOnlyList<CategorySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategorySummary(c.Id, c.Name, c.Description, c.Feeds.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<ErrorOr<CategorySummary>> CreateAsync(
        CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = new CreateCategoryRequest(
            request.Name?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return validation.ToErrors();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await NameExistsAsync(db, normalized.Name, cancellationToken))
            return CategoryErrors.DuplicateName(normalized.Name);

        var category = new Category { Name = normalized.Name, Description = normalized.Description };
        db.Categories.Add(category);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent create on the unique Name index; anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await NameExistsAsync(check, normalized.Name, cancellationToken))
                return CategoryErrors.DuplicateName(normalized.Name);
            throw;
        }

        return new CategorySummary(category.Id, category.Name, category.Description, FeedCount: 0);
    }

    // The unique index is case-sensitive in SQLite, so the friendlier case-insensitive rule lives here. ToLower()
    // on the column means this check scans rather than seeks — fine for a handful of categories, and it keeps the
    // same semantics on a case-insensitive provider (SQL Server) without a collation change (see FeedService).
    private static Task<bool> NameExistsAsync(ApplicationDbContext db, string name, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return db.Categories.AnyAsync(c => c.Name.ToLower() == lowered, cancellationToken);
    }
}
