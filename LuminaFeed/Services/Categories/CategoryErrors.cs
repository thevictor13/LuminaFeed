using ErrorOr;

namespace LuminaFeed.Services.Categories;

public static class CategoryErrors
{
    public static Error DuplicateName(string name) =>
        Error.Conflict("Category.DuplicateName", $"A category named '{name}' already exists.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("Category.NotFound", $"Category '{id}' was not found.");

    public static Error HasFeeds(Guid id, int feedCount) =>
        Error.Conflict(
            "Category.HasFeeds",
            $"This category still has {feedCount} feed(s). Move or delete them before deleting the category.");
}
