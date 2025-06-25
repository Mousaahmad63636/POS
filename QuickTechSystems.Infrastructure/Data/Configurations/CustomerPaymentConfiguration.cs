using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
    {
        public void Configure(EntityTypeBuilder<CustomerPayment> builder)
        {
            builder.HasKey(cp => cp.PaymentId);

            builder.Property(cp => cp.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(cp => cp.PaymentMethod)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(cp => cp.Notes)
                .HasMaxLength(500);

            builder.Property(cp => cp.Status)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(cp => cp.CreatedBy)
                .HasMaxLength(100)
                .IsRequired();

            // Relationships
            builder.HasOne(cp => cp.Customer)
                .WithMany()
                .HasForeignKey(cp => cp.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(cp => cp.DrawerTransaction)
                .WithMany()
                .HasForeignKey(cp => cp.DrawerTransactionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(cp => cp.CustomerId);
            builder.HasIndex(cp => cp.PaymentDate);
            builder.HasIndex(cp => cp.Status);
            builder.HasIndex(cp => cp.DrawerTransactionId);
        }
    }
}