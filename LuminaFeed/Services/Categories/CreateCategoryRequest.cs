using FluentValidation;

namespace LuminaFeed.Services.Categories;

/// <summary>Admin request to add a category.</summary>
public sealed record CreateCategoryRequest(string Name, string? Description);

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    // Mirror the column limits in CategoryConfiguration (asserted by CategoryServiceTests).
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(DescriptionMaxLength);
    }
}
