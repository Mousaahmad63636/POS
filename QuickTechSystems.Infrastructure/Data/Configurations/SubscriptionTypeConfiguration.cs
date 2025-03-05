// SubscriptionTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    public class SubscriptionTypeConfiguration : IEntityTypeConfiguration<SubscriptionType>
    {
        public void Configure(EntityTypeBuilder<SubscriptionType> builder)
        {
            builder.ToTable("SubscriptionTypes");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Description)
                .HasMaxLength(500);

            builder.Property(x => x.AdditionalCharge)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            builder.Property(x => x.IsActive)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            // Index for faster lookups
            builder.HasIndex(x => x.Name).IsUnique();
        }
    }
}