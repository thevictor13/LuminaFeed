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

        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.FeedUrl).IsRequired().HasMaxLength(2048);
        builder.Property(f => f.SiteUrl).IsRequired().HasMaxLength(2048);
        builder.Property(f => f.ImageUrl).HasMaxLength(2048);
        builder.Property(f => f.Description).HasMaxLength(1000);
        builder.Property(f => f.ETag).HasMaxLength(512);
        builder.Property(f => f.LastModified).HasMaxLength(256);

        builder.HasIndex(f => f.FeedUrl).IsUnique();

        builder.HasMany(f => f.Articles)
            .WithOne(a => a.Feed)
            .HasForeignKey(a => a.FeedId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.Subscriptions)
            .WithOne(s => s.Feed)
            .HasForeignKey(s => s.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
