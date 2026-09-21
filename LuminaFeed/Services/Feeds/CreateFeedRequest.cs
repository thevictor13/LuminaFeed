using FluentValidation;

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
    // Mirror the column limits in FeedConfiguration (asserted by FeedServiceTests).
    public const int NameMaxLength = 200;
    public const int UrlMaxLength = 2048;
    public const int DescriptionMaxLength = 1000;

    public CreateFeedRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(NameMaxLength);
        RuleFor(r => r.CategoryId).NotEmpty().WithMessage("A category must be selected.");

        // Stop at the first failure per URL so an empty field isn't also reported as "not a URL".
        RuleFor(r => r.FeedUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Feed Url' must be an absolute http(s) URL.");
        RuleFor(r => r.SiteUrl).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Site Url' must be an absolute http(s) URL.");
        RuleFor(r => r.ImageUrl).MaximumLength(UrlMaxLength)
            .Must(BeAbsoluteHttpUrl).WithMessage("'Image Url' must be an absolute http(s) URL.")
            .When(r => !string.IsNullOrEmpty(r.ImageUrl));

        RuleFor(r => r.Description).MaximumLength(DescriptionMaxLength);
        RuleFor(r => r.Popularity).GreaterThanOrEqualTo(0);
    }

    private static bool BeAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
