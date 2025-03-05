using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class CustomerSubscriptionConfiguration : IEntityTypeConfiguration<CustomerSubscription>
    {
        public void Configure(EntityTypeBuilder<CustomerSubscription> builder)
        {
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(e => e.PhoneNumber)
                .HasMaxLength(20);

            builder.Property(e => e.OldCounter)
                .HasPrecision(18, 2);

            builder.Property(e => e.NewCounter)
                .HasPrecision(18, 2);

            builder.Property(e => e.BillAmount)
                .HasPrecision(18, 2);

            builder.Property(e => e.LastBillDate)
                .IsRequired();

            builder.Property(e => e.CreatedAt)
                .IsRequired();
            builder.Property(e => e.LastBillDate)
            .HasDefaultValueSql("GETDATE()");

            builder.Property(e => e.OldCounter)
                .HasDefaultValue(0.0m);

            builder.Property(e => e.NewCounter)
                .HasDefaultValue(0.0m);

            builder.Property(e => e.BillAmount)
                .HasDefaultValue(0.0m);

            // Add check constraint to prevent negative consumption
            builder.HasCheckConstraint("CK_CustomerSubscription_ValidCounters",
                "[NewCounter] >= [OldCounter]");
            // Indexes
            builder.HasIndex(e => e.PhoneNumber);
            builder.HasIndex(e => e.LastBillDate);

            builder.HasOne(x => x.SubscriptionType)
        .WithMany(x => x.CustomerSubscriptions)
        .HasForeignKey(x => x.SubscriptionTypeId)
        .OnDelete(DeleteBehavior.SetNull);
        }
    }
}