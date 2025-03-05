// MonthlySubscriptionSettingsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class MonthlySubscriptionSettingsConfiguration : IEntityTypeConfiguration<MonthlySubscriptionSettings>
    {
        public void Configure(EntityTypeBuilder<MonthlySubscriptionSettings> builder)
        {
            builder.ToTable("MonthlySubscriptionSettings");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.DefaultUnitPrice)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            builder.Property(x => x.LastModified)
                .IsRequired();

            builder.Property(x => x.ModifiedBy)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Notes)
                .HasMaxLength(500);

            builder.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();
        }
    }
}