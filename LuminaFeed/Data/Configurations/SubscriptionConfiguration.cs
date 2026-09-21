using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuminaFeed.Data.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // 450 matches ASP.NET Identity's AspNetUsers.Id key width so this column can back the
        // (UserId, FeedId) unique index on any provider (SQL Server rejects unbounded strings in an index).
        builder.Property(s => s.UserId).IsRequired().HasMaxLength(450);
        builder.Property(s => s.SlackWebhookUrl).HasMaxLength(2048);

        // One subscription per (user, feed).
        builder.HasIndex(s => new { s.UserId, s.FeedId }).IsUnique();

        builder.HasOne(s => s.User)
            .WithMany(u => u.Subscriptions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Feed relationship (and cascade) is configured from FeedConfiguration.
    }
}
