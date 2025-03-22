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

            // Remove any check constraint on CurrentStock if it exists
            // Either remove this line entirely OR ensure it doesn't have a check constraint:
            builder.Property(p => p.CurrentStock);
            // No .HasAnnotation("SqlServer:Check", "CurrentStock >= 0") constraint

            builder.HasIndex(p => p.Barcode)
                .IsUnique();
            builder.Property(p => p.Speed)
                .HasMaxLength(50)
                .IsRequired(false);  // Makes it optional
            builder.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Property(p => p.Image)
                .IsRequired(false);
        }
    }
}