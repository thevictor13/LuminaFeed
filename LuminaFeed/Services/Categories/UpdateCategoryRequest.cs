using FluentValidation;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Categories;

/// <summary>Admin request to edit an existing category.</summary>
public sealed record UpdateCategoryRequest(Guid Id, string Name, string? Description);

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        // Length limits are the entity's own column limits, so they can't drift from the schema.
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Category.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(Category.DescriptionMaxLength);
    }
}
