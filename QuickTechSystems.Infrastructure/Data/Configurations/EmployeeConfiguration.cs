using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Infrastructure.Data.Configurations
{
    // Infrastructure/Data/Configurations/EmployeeConfiguration.cs
    public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
    {
        public void Configure(EntityTypeBuilder<Employee> builder)
        {
            builder.HasKey(e => e.EmployeeId);

            builder.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(e => e.Role)
                .IsRequired()
                .HasMaxLength(20);

            builder.HasIndex(e => e.Username)
                .IsUnique();

            builder.Property(e => e.MonthlySalary)
    .HasPrecision(18, 2)
    .HasDefaultValue(0);

            builder.Property(e => e.CurrentBalance)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);
            // Configuration for the new SecureCode property
            builder.Property(e => e.SecureCode)
                .HasMaxLength(20);

            // Create a unique index for SecureCode
            builder.HasIndex(e => e.SecureCode)
                .IsUnique()
                .HasFilter("[SecureCode] IS NOT NULL"); // SQL Server specific filter for NULL values

            // Unique username constraint
            builder.HasIndex(e => e.Username)
                .IsUnique();
        }
    }
}
