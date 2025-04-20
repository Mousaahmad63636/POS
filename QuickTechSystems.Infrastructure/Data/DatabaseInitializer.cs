using System.Text;
using QuickTechSystems.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace QuickTechSystems.Infrastructure.Data
{
    public static class DatabaseInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            context.Database.EnsureCreated();

            // Add seed data if the database is empty
            if (!context.Categories.Any())
            {
                var categories = new[]
                {
                    new Category { Name = "General", Description = "General items" },
                    new Category { Name = "Electronics", Description = "Electronic items" },
                    new Category { Name = "Groceries", Description = "Grocery items" }
                };

                context.Categories.AddRange(categories);
                context.SaveChanges();
            }




        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        // QuickTechSystems.Infrastructure/Data/DatabaseInitializer.cs
        public static void SeedDefaultAdmin(ApplicationDbContext context)
        {
            if (!context.Employees.Any())
            {
                var admin = new Employee
                {
                    Username = "admin",
                    PasswordHash = HashPassword("admin123"), // Default password: admin123
                    FirstName = "System",
                    LastName = "Administrator",
                    Role = "Admin",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    SecureCode = "123456" // Default secure code for the admin
                };

                context.Employees.Add(admin);
                context.SaveChanges();
            }
        }

    }
}