using ErrorOr;
using FluentValidation;
using LuminaFeed.Data;
using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;

namespace LuminaFeed.Services.Categories;

public sealed class CategoryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IValidator<CreateCategoryRequest> validator,
    IValidator<UpdateCategoryRequest> updateValidator) : ICategoryService
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

    public async Task<ErrorOr<CategorySummary>> UpdateAsync(
        UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        };

        var validation = await updateValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return validation.ToErrors();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == normalized.Id, cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound(normalized.Id);

        if (await NameExistsAsync(db, normalized.Name, cancellationToken, excludingId: normalized.Id))
            return CategoryErrors.DuplicateName(normalized.Name);

        category.Name = normalized.Name;
        category.Description = normalized.Description;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent rename onto the unique Name index; anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await NameExistsAsync(check, normalized.Name, cancellationToken, excludingId: normalized.Id))
                return CategoryErrors.DuplicateName(normalized.Name);
            throw;
        }

        var feedCount = await db.Feeds.CountAsync(f => f.CategoryId == category.Id, cancellationToken);
        return new CategorySummary(category.Id, category.Name, category.Description, feedCount);
    }

    public async Task<ErrorOr<Deleted>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound(id);

        // Feed → Category is Restrict at the DB, so a category holding feeds can't be deleted; report it as a
        // conflict rather than letting SaveChanges throw.
        var feedCount = await db.Feeds.CountAsync(f => f.CategoryId == id, cancellationToken);
        if (feedCount > 0)
            return CategoryErrors.HasFeeds(id, feedCount);

        db.Categories.Remove(category);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a feed concurrently assigned to this category between the count above and the
            // save: the Restrict FK trips. Re-check and report the same conflict; anything else is a real fault.
            await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
            var raced = await check.Feeds.CountAsync(f => f.CategoryId == id, cancellationToken);
            if (raced > 0)
                return CategoryErrors.HasFeeds(id, raced);
            throw;
        }

        return Result.Deleted;
    }

    // The unique index is case-sensitive in SQLite, so the friendlier case-insensitive rule lives here. ToLower()
    // on the column means this check scans rather than seeks — fine for a handful of categories, and it keeps the
    // same semantics on a case-insensitive provider (SQL Server) without a collation change (see FeedService).
    // Editing passes its own id as excludingId so a category doesn't collide with itself.
    private static Task<bool> NameExistsAsync(
        ApplicationDbContext db, string name, CancellationToken cancellationToken, Guid? excludingId = null)
    {
        var lowered = name.ToLowerInvariant();
        return db.Categories.AnyAsync(
            c => c.Name.ToLower() == lowered && (excludingId == null || c.Id != excludingId), cancellationToken);
    }
}
