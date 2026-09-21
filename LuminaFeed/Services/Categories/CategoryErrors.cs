using ErrorOr;

namespace LuminaFeed.Services.Categories;

public static class CategoryErrors
{
    public static Error DuplicateName(string name) =>
        Error.Conflict("Category.DuplicateName", $"A category named '{name}' already exists.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("Category.NotFound", $"Category '{id}' was not found.");
}
