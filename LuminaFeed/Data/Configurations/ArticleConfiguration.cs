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

        // Per-feed de-dup of source items. Its FeedId prefix also serves the feed page's "articles for this feed"
        // lookup (B4); the "latest" ordering is done in memory because SQLite/EF cannot ORDER BY a DateTimeOffset.
        builder.HasIndex(a => new { a.FeedId, a.ExternalId }).IsUnique();

        // Feed relationship (and cascade) is configured from FeedConfiguration.
    }
}
