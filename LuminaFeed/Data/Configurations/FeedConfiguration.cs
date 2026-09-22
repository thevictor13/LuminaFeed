using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuminaFeed.Data.Configurations;

public sealed class FeedConfiguration : IEntityTypeConfiguration<Feed>
{
    public void Configure(EntityTypeBuilder<Feed> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name).IsRequired().HasMaxLength(Feed.NameMaxLength);
        builder.Property(f => f.FeedUrl).IsRequired().HasMaxLength(Feed.UrlMaxLength);
        builder.Property(f => f.SiteUrl).IsRequired().HasMaxLength(Feed.UrlMaxLength);
        builder.Property(f => f.ImageUrl).HasMaxLength(Feed.UrlMaxLength);
        builder.Property(f => f.Description).HasMaxLength(Feed.DescriptionMaxLength);
        builder.Property(f => f.ETag).HasMaxLength(Feed.ETagMaxLength);
        builder.Property(f => f.LastModified).HasMaxLength(Feed.LastModifiedMaxLength);

        builder.HasIndex(f => f.FeedUrl).IsUnique();

        // Supports the public list's default ordering: feeds by Popularity within a Category.
        builder.HasIndex(f => new { f.CategoryId, f.Popularity });

        builder.HasMany(f => f.Articles)
            .WithOne(a => a.Feed)
            .HasForeignKey(a => a.FeedId)
            .OnDelete(DeleteBehavior.Cascade);

        // ClientCascade (not DB Cascade): Subscription is reachable from both User and Feed, and two
        // database cascade paths into one table are rejected by SQL Server. The app deletes feeds
        // through EF (loading Subscriptions), so EF performs the cascade for tracked subscriptions
        // while User remains the single database-level cascade owner of Subscription.
        builder.HasMany(f => f.Subscriptions)
            .WithOne(s => s.Feed)
            .HasForeignKey(s => s.FeedId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
