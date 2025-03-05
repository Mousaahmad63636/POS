using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Infrastructure.Data;

namespace QuickTechSystems.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private IGenericRepository<Product>? _products;
        private IGenericRepository<Category>? _categories;
        private IGenericRepository<Customer>? _customers;
        private IGenericRepository<Transaction>? _transactions;
        private IGenericRepository<BusinessSetting>? _businessSettings;
        private IGenericRepository<SystemPreference>? _systemPreferences;
        private IGenericRepository<Supplier>? _suppliers;
        private IGenericRepository<Expense>? _expenses;
        private IGenericRepository<Employee>? _employees;
        private IGenericRepository<Drawer>? _drawers;
        private IGenericRepository<CustomerSubscription>? _customerSubscriptions;
        private IGenericRepository<SubscriptionType>? _subscriptiontypes;
        private IGenericRepository<MonthlySubscriptionSettings>? _monthlysettings;
        private bool _disposed;

        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;
        }


        public IGenericRepository<SubscriptionType> SubscriptionTypes =>
    _subscriptiontypes ??= new GenericRepository<SubscriptionType>(_context);

        public IGenericRepository<MonthlySubscriptionSettings> MonthlySubscriptionSettings =>
            _monthlysettings ??= new GenericRepository<MonthlySubscriptionSettings>(_context);
        public IGenericRepository<CustomerSubscription> CustomerSubscriptions =>
            _customerSubscriptions ??= new GenericRepository<CustomerSubscription>(_context);

        public IGenericRepository<Employee> Employees =>
            _employees ??= new GenericRepository<Employee>(_context);

        public IGenericRepository<Product> Products =>
            _products ??= new GenericRepository<Product>(_context);

        public IGenericRepository<Category> Categories =>
            _categories ??= new GenericRepository<Category>(_context);

        public IGenericRepository<Customer> Customers =>
            _customers ??= new GenericRepository<Customer>(_context);

        public IGenericRepository<Transaction> Transactions =>
            _transactions ??= new GenericRepository<Transaction>(_context);

        public IGenericRepository<BusinessSetting> BusinessSettings =>
            _businessSettings ??= new GenericRepository<BusinessSetting>(_context);

        public IGenericRepository<SystemPreference> SystemPreferences =>
            _systemPreferences ??= new GenericRepository<SystemPreference>(_context);

        public IGenericRepository<Expense> Expenses =>
            _expenses ??= new GenericRepository<Expense>(_context);

        public IGenericRepository<Drawer> Drawers =>
            _drawers ??= new GenericRepository<Drawer>(_context);

        public IGenericRepository<Supplier> Suppliers =>
            _suppliers ??= new GenericRepository<Supplier>(_context);

        public DbContext Context => _context;

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(_context.Database.GetDbConnection().ConnectionString);
            var context = new ApplicationDbContext(optionsBuilder.Options);
            return await context.Database.BeginTransactionAsync();
        }
        private IGenericRepository<T> GetOrCreateRepository<T>() where T : class
        {
            var fieldName = $"_{typeof(T).Name.ToLower()}s";
            var field = GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                var repository = field.GetValue(this) as IGenericRepository<T>;
                if (repository == null)
                {
                    repository = new GenericRepository<T>(_context);
                    field.SetValue(this, repository);
                }
                return repository;
            }

            return new GenericRepository<T>(_context);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _context.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}