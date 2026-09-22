using FluentValidation;
using LuminaFeed.Domain;

namespace LuminaFeed.Services.Feeds;

/// <summary>Admin request to add a feed to a category.</summary>
public sealed record CreateFeedRequest(
    string Name,
    Guid CategoryId,
    string FeedUrl,
    string SiteUrl,
    string? ImageUrl = null,
    string? Description = null,
    int Popularity = 0);

public sealed class CreateFeedRequestValidator : AbstractValidator<CreateFeedRequest>
{
    public CreateFeedRequestValidator()
    {
        // Length limits are the entity's own column limits, so they can't drift from the schema.
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Feed.NameMaxLength);
        RuleFor(r => r.CategoryId).NotEmpty().WithMessage("A category must be selected.");

        // Stop at the first failure per URL so an empty field isn't also reported as "not a URL".
        RuleFor(r => r.FeedUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(Feed.UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Feed Url' must be an absolute http(s) URL.");
        RuleFor(r => r.SiteUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(Feed.UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Site Url' must be an absolute http(s) URL.");
        RuleFor(r => r.ImageUrl).MaximumLength(Feed.UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Image Url' must be an absolute http(s) URL.")
            .When(r => !string.IsNullOrEmpty(r.ImageUrl));

        RuleFor(r => r.Description).MaximumLength(Feed.DescriptionMaxLength);
        RuleFor(r => r.Popularity).GreaterThanOrEqualTo(0);
    }

    /// <summary>The one URL rule for admin-supplied values: absolute and http(s), so nothing else reaches a page or a fetch.</summary>
    internal static bool BeAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
