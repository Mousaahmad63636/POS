using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.HasKey(p => p.ProductId);
            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);
            builder.Property(p => p.Barcode)
                .HasMaxLength(50);
            builder.Property(p => p.Description)
                .HasMaxLength(500);
            builder.Property(p => p.PurchasePrice)
                .HasPrecision(18, 2);
            builder.Property(p => p.SalePrice)
                .HasPrecision(18, 2);

            builder.Property(p => p.CurrentStock);

            builder.HasIndex(p => p.Barcode)
                .IsUnique();
            builder.Property(p => p.Speed)
                .HasMaxLength(50)
                .IsRequired(false);

            // Update image config to store path
            builder.Property(p => p.ImagePath)
                .HasMaxLength(500)
                .IsRequired(false);

            builder.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}