using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuminaFeed.Data.Configurations;

public sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ExternalId).IsRequired().HasMaxLength(Article.ExternalIdMaxLength);
        builder.Property(a => a.Title).IsRequired().HasMaxLength(Article.TitleMaxLength);
        builder.Property(a => a.Link).IsRequired().HasMaxLength(Article.UrlMaxLength);
        builder.Property(a => a.ImageUrl).HasMaxLength(Article.UrlMaxLength);

        // Per-feed de-dup of source items.
        builder.HasIndex(a => new { a.FeedId, a.ExternalId }).IsUnique();

        // Feed relationship (and cascade) is configured from FeedConfiguration.
    }
}
