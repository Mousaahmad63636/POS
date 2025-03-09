using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class TransactionDetailConfiguration : IEntityTypeConfiguration<TransactionDetail>
    {
        public void Configure(EntityTypeBuilder<TransactionDetail> builder)
        {
            builder.HasKey(td => td.TransactionDetailId);

            builder.Property(td => td.UnitPrice)
                .HasPrecision(18, 2);

            builder.Property(td => td.PurchasePrice)
                .HasPrecision(18, 2);

            builder.Property(td => td.Discount)
                .HasPrecision(18, 2);

            builder.Property(td => td.Total)
                .HasPrecision(18, 2);

            builder.Property(td => td.ReturnedQuantity)
                .HasDefaultValue(0);

            builder.Property(td => td.IsReturned)
                .HasDefaultValue(false);

            builder.Property(td => td.ReturnReason)
                .HasMaxLength(500);

            builder.HasOne(td => td.Product)
                .WithMany(p => p.TransactionDetails)
                .HasForeignKey(td => td.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}