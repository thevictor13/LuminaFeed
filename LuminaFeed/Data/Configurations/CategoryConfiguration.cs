using LuminaFeed.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuminaFeed.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(Category.NameMaxLength);
        builder.HasIndex(c => c.Name).IsUnique();

        builder.Property(c => c.Description).HasMaxLength(Category.DescriptionMaxLength);

        builder.HasMany(c => c.Feeds)
            .WithOne(f => f.Category)
            .HasForeignKey(f => f.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
