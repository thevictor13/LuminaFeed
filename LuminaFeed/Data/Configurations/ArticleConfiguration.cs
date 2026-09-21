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

        builder.Property(a => a.ExternalId).IsRequired().HasMaxLength(1024);
        builder.Property(a => a.Title).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Link).IsRequired().HasMaxLength(2048);
        builder.Property(a => a.ImageUrl).HasMaxLength(2048);

        // Per-feed de-dup of source items.
        builder.HasIndex(a => new { a.FeedId, a.ExternalId }).IsUnique();

        // Feed relationship (and cascade) is configured from FeedConfiguration.
    }
}
