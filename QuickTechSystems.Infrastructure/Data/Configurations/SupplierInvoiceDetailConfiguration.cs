using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class SupplierInvoiceDetailConfiguration : IEntityTypeConfiguration<SupplierInvoiceDetail>
    {
        public void Configure(EntityTypeBuilder<SupplierInvoiceDetail> builder)
        {
            builder.HasKey(sid => sid.SupplierInvoiceDetailId);

            builder.Property(sid => sid.Quantity)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(sid => sid.PurchasePrice)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(sid => sid.TotalPrice)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(sid => sid.BoxBarcode)
                .HasMaxLength(50)
                .HasDefaultValue(string.Empty);

            builder.Property(sid => sid.NumberOfBoxes)
                .HasDefaultValue(0);

            builder.Property(sid => sid.ItemsPerBox)
                .HasDefaultValue(1);

            builder.Property(sid => sid.BoxPurchasePrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.BoxSalePrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.CurrentStock)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.Storehouse)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.SalePrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.WholesalePrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.BoxWholesalePrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            builder.Property(sid => sid.MinimumStock)
                .HasDefaultValue(0);

            builder.HasOne(sid => sid.SupplierInvoice)
                .WithMany(si => si.Details)
                .HasForeignKey(sid => sid.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(sid => sid.Product)
                .WithMany()
                .HasForeignKey(sid => sid.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(sid => new { sid.SupplierInvoiceId, sid.ProductId })
                .HasDatabaseName("IX_SupplierInvoiceDetails_Invoice_Product");

            builder.HasIndex(sid => sid.ProductId)
                .HasDatabaseName("IX_SupplierInvoiceDetails_ProductId");
        }
    }
}