using FluentValidation;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Feeds;

/// <summary>Admin request to edit an existing feed.</summary>
public sealed record UpdateFeedRequest(
    Guid Id,
    string Name,
    Guid CategoryId,
    string FeedUrl,
    string SiteUrl,
    string? ImageUrl = null,
    string? Description = null,
    int Popularity = 0);

public sealed class UpdateFeedRequestValidator : AbstractValidator<UpdateFeedRequest>
{
    public UpdateFeedRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();

        // Length limits are the entity's own column limits, so they can't drift from the schema. The URL rule is
        // shared with the create request so both paths reject anything but an absolute http(s) URL.
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Feed.NameMaxLength);
        RuleFor(r => r.CategoryId).NotEmpty().WithMessage("A category must be selected.");

        RuleFor(r => r.FeedUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(Feed.UrlMaxLength)
            .Must(CreateFeedRequestValidator.BeAbsoluteHttpUrl).WithMessage("'Feed Url' must be an absolute http(s) URL.");
        RuleFor(r => r.SiteUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(Feed.UrlMaxLength)
            .Must(CreateFeedRequestValidator.BeAbsoluteHttpUrl).WithMessage("'Site Url' must be an absolute http(s) URL.");
        RuleFor(r => r.ImageUrl).MaximumLength(Feed.UrlMaxLength)
            .Must(CreateFeedRequestValidator.BeAbsoluteHttpUrl).WithMessage("'Image Url' must be an absolute http(s) URL.")
            .When(r => !string.IsNullOrEmpty(r.ImageUrl));

        RuleFor(r => r.Description).MaximumLength(Feed.DescriptionMaxLength);
        RuleFor(r => r.Popularity).GreaterThanOrEqualTo(0);
    }
}
