using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Infrastructure.Data.Configurations;
using System.Reflection;

namespace QuickTechSystems.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<TransactionDetail> TransactionDetails => Set<TransactionDetail>();
        public DbSet<InventoryHistory> InventoryHistories => Set<InventoryHistory>();
        public DbSet<BusinessSetting> BusinessSettings => Set<BusinessSetting>();
        public DbSet<SystemPreference> SystemPreferences => Set<SystemPreference>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<SupplierTransaction> SupplierTransactions => Set<SupplierTransaction>();
        public DbSet<Expense> Expenses => Set<Expense>();
        public DbSet<Drawer> Drawers => Set<Drawer>();
        public DbSet<CustomerSubscription> CustomerSubscriptions => Set<CustomerSubscription>();
        public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
        public DbSet<CounterHistory> CounterHistories => Set<CounterHistory>();
        public DbSet<DrawerHistoryEntry> DrawerHistory => Set<DrawerHistoryEntry>();
        public DbSet<DrawerTransaction> DrawerTransactions => Set<DrawerTransaction>();
        public DbSet<MonthlySubscriptionSettings> MonthlySubscriptionSettings { get; set; }
        public DbSet<SubscriptionType> SubscriptionTypes { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            modelBuilder.ApplyConfiguration(new MonthlySubscriptionSettingsConfiguration());
            modelBuilder.ApplyConfiguration(new SubscriptionTypeConfiguration());
        }
    }
}