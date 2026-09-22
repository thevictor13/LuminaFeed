using FluentValidation;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Categories;

/// <summary>Admin request to add a category.</summary>
public sealed record CreateCategoryRequest(string Name, string? Description);

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        // Length limits are the entity's own column limits, so they can't drift from the schema.
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Category.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Category.DescriptionMaxLength);
    }
}
