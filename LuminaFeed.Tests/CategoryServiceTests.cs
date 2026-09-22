using ErrorOr;
using LuminaFeed.Domain;
using LuminaFeed.Services.Categories;

namespace LuminaFeed.Tests;

/// <summary>Covers S1 category create/list against real in-memory SQLite.</summary>
public sealed class CategoryServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private CategoryService CreateService() =>
        new(_db, new CreateCategoryRequestValidator(), new UpdateCategoryRequestValidator());

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_PersistsTrimmedCategory_WithVersion7Id()
    {
        var result = await CreateService().CreateAsync(new CreateCategoryRequest("  Science  ", "  Discoveries.  "));

        Assert.False(result.IsError);
        Assert.Equal("Science", result.Value.Name);
        Assert.Equal("Discoveries.", result.Value.Description);
        Assert.Equal(0, result.Value.FeedCount);
        Assert.Equal(7, result.Value.Id.Version);

        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Categories);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("Science", stored.Name);
    }

    [Fact]
    public async Task CreateAsync_BlankDescription_IsStoredAsNull()
    {
        var result = await CreateService().CreateAsync(new CreateCategoryRequest("Science", "   "));

        Assert.False(result.IsError);
        Assert.Null(result.Value.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateAsync_MissingName_ReturnsValidationError(string? name)
    {
        var result = await CreateService().CreateAsync(new CreateCategoryRequest(name!, null));

        Assert.True(result.IsError);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(CreateCategoryRequest.Name), error.Code);

        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Categories);
    }

    [Fact]
    public async Task CreateAsync_OverlongFields_ReturnOneValidationErrorEach()
    {
        var result = await CreateService().CreateAsync(new CreateCategoryRequest(
            new string('n', Category.NameMaxLength + 1),
            new string('d', Category.DescriptionMaxLength + 1)));

        Assert.True(result.IsError);
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        Assert.Equal(
            [nameof(CreateCategoryRequest.Description), nameof(CreateCategoryRequest.Name)],
            result.Errors.Select(e => e.Code).Order());
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_IsAConflict_IgnoringCaseAndPadding()
    {
        _db.AddCategory("World News");

        var result = await CreateService().CreateAsync(new CreateCategoryRequest(" world NEWS ", null));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Category.DuplicateName", result.FirstError.Code);

        using var ctx = _db.CreateDbContext();
        Assert.Single(ctx.Categories);
    }

    [Fact]
    public async Task ListAsync_ReturnsCategoriesByName_WithFeedCounts()
    {
        var weather = _db.AddCategory("Weather");
        _db.AddCategory("Markets");
        _db.AddFeed(weather.Id, "Met Office");
        _db.AddFeed(weather.Id, "NOAA");

        var list = await CreateService().ListAsync();

        Assert.Equal(["Markets", "Weather"], list.Select(c => c.Name));
        Assert.Equal([0, 2], list.Select(c => c.FeedCount));
    }

    [Fact]
    public async Task ListAsync_EmptyDatabase_ReturnsEmpty()
    {
        Assert.Empty(await CreateService().ListAsync());
    }

    // --- Update (A1) ------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_PersistsTrimmedChanges()
    {
        var category = _db.AddCategory("Scince", "typo");

        var result = await CreateService().UpdateAsync(
            new UpdateCategoryRequest(category.Id, "  Science  ", "  Discoveries.  "));

        Assert.False(result.IsError);
        Assert.Equal(category.Id, result.Value.Id);
        Assert.Equal("Science", result.Value.Name);
        Assert.Equal("Discoveries.", result.Value.Description);

        using var ctx = _db.CreateDbContext();
        var stored = Assert.Single(ctx.Categories);
        Assert.Equal("Science", stored.Name);
        Assert.Equal("Discoveries.", stored.Description);
    }

    [Fact]
    public async Task UpdateAsync_BlankDescription_IsStoredAsNull()
    {
        var category = _db.AddCategory("Science", "old");

        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(category.Id, "Science", "   "));

        Assert.False(result.IsError);
        Assert.Null(result.Value.Description);
    }

    [Fact]
    public async Task UpdateAsync_KeepsTheFeedCountInTheSummary()
    {
        var category = _db.AddCategory("Science");
        _db.AddFeed(category.Id, "Nature");
        _db.AddFeed(category.Id, "New Scientist");

        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(category.Id, "Sciences", null));

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.FeedCount);
    }

    [Fact]
    public async Task UpdateAsync_RenameOntoAnotherCategory_IsAConflict_IgnoringCaseAndPadding()
    {
        _db.AddCategory("World News");
        var markets = _db.AddCategory("Markets");

        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(markets.Id, " world NEWS ", null));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Category.DuplicateName", result.FirstError.Code);

        using var ctx = _db.CreateDbContext();
        Assert.Equal("Markets", ctx.Categories.Single(c => c.Id == markets.Id).Name);
    }

    [Fact]
    public async Task UpdateAsync_RenamingACategoryToACaseVariantOfItsOwnName_IsAllowed()
    {
        var category = _db.AddCategory("Science");

        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(category.Id, "SCIENCE", null));

        Assert.False(result.IsError);
        Assert.Equal("SCIENCE", result.Value.Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_IsNotFound()
    {
        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(Guid.CreateVersion7(), "Science", null));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Category.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task UpdateAsync_MissingName_ReturnsValidationError()
    {
        var category = _db.AddCategory("Science");

        var result = await CreateService().UpdateAsync(new UpdateCategoryRequest(category.Id, "  ", null));

        Assert.True(result.IsError);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(nameof(UpdateCategoryRequest.Name), error.Code);
    }

    // --- Delete (A1) ------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_CategoryWithoutFeeds_RemovesIt()
    {
        var category = _db.AddCategory("Science");

        var result = await CreateService().DeleteAsync(category.Id);

        Assert.False(result.IsError);
        using var ctx = _db.CreateDbContext();
        Assert.Empty(ctx.Categories);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithFeeds_IsAConflict_AndKeepsIt()
    {
        var category = _db.AddCategory("Science");
        _db.AddFeed(category.Id, "Nature");

        var result = await CreateService().DeleteAsync(category.Id);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Category.HasFeeds", result.FirstError.Code);

        using var ctx = _db.CreateDbContext();
        Assert.Single(ctx.Categories);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNotFound()
    {
        var result = await CreateService().DeleteAsync(Guid.CreateVersion7());

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Category.NotFound", result.FirstError.Code);
    }
}
