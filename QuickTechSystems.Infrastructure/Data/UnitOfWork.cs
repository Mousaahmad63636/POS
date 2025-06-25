using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace QuickTechSystems.Infrastructure.Data
{
    public class UnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private ApplicationDbContext _context;
        private bool _disposed;
        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);

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
        private IGenericRepository<InventoryHistory>? _inventoryHistories;
        private IGenericRepository<RestaurantTable>? _restaurantTables;
        private IGenericRepository<SupplierInvoice>? _supplierInvoices;
        private IGenericRepository<SupplierInvoiceDetail>? _supplierInvoiceDetails;
        private IGenericRepository<CustomerPayment>? _customerPayments;

        public IGenericRepository<InventoryHistory> InventoryHistories =>
            _inventoryHistories ??= CreateRepository<InventoryHistory>();

        public IGenericRepository<SupplierInvoice> SupplierInvoices =>
            _supplierInvoices ??= CreateRepository<SupplierInvoice>();

        public IGenericRepository<SupplierInvoiceDetail> SupplierInvoiceDetails =>
            _supplierInvoiceDetails ??= CreateRepository<SupplierInvoiceDetail>();

        public IGenericRepository<RestaurantTable> RestaurantTables =>
            _restaurantTables ??= CreateRepository<RestaurantTable>();

        public IGenericRepository<Employee> Employees =>
            _employees ??= CreateRepository<Employee>();

        public IGenericRepository<Product> Products =>
            _products ??= CreateRepository<Product>();

        public IGenericRepository<Category> Categories =>
            _categories ??= CreateRepository<Category>();

        public IGenericRepository<Customer> Customers =>
            _customers ??= CreateRepository<Customer>();

        public IGenericRepository<Transaction> Transactions =>
            _transactions ??= CreateRepository<Transaction>();

        public IGenericRepository<BusinessSetting> BusinessSettings =>
            _businessSettings ??= CreateRepository<BusinessSetting>();

        public IGenericRepository<SystemPreference> SystemPreferences =>
            _systemPreferences ??= CreateRepository<SystemPreference>();

        public IGenericRepository<Expense> Expenses =>
            _expenses ??= CreateRepository<Expense>();

        public IGenericRepository<Drawer> Drawers =>
            _drawers ??= CreateRepository<Drawer>();

        public IGenericRepository<Supplier> Suppliers =>
            _suppliers ??= CreateRepository<Supplier>();

        public IGenericRepository<CustomerPayment> CustomerPayments =>
            _customerPayments ??= CreateRepository<CustomerPayment>();

        public DbContext Context => GetOrCreateContext();

        public UnitOfWork(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            _context = _contextFactory.CreateDbContext();
        }

        private ApplicationDbContext GetOrCreateContext()
        {
            if (_context == null || _context.ChangeTracker.Entries().Any(e => e.State == EntityState.Detached))
            {
                _context?.Dispose();
                _context = _contextFactory.CreateDbContext();
                ResetRepositories();
            }
            return _context;
        }

        private IGenericRepository<T> CreateRepository<T>() where T : class
        {
            return new GenericRepository<T>(GetOrCreateContext(), _contextFactory);
        }

        public void DetachEntity<T>(T entity) where T : class
        {
            if (entity == null) return;

            var context = GetOrCreateContext();
            var entry = context.Entry(entity);
            if (entry != null)
            {
                entry.State = EntityState.Detached;
            }
        }

        public void DetachAllEntities()
        {
            var context = GetOrCreateContext();
            var entries = context.ChangeTracker.Entries().ToList();
            foreach (var entry in entries)
            {
                entry.State = EntityState.Detached;
            }
        }

        public async Task<int> SaveChangesAsync()
        {
            await _contextLock.WaitAsync();
            try
            {
                var context = GetOrCreateContext();
                return await context.SaveChangesAsync();
            }
            catch (ObjectDisposedException)
            {
                _context = _contextFactory.CreateDbContext();
                ResetRepositories();
                return await _context.SaveChangesAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("disposed") || ex.Message.Contains("second operation"))
            {
                await Task.Delay(50);
                _context = _contextFactory.CreateDbContext();
                ResetRepositories();
                return await _context.SaveChangesAsync();
            }
            finally
            {
                _contextLock.Release();
            }
        }

        private void ResetRepositories()
        {
            _restaurantTables = null;
            _products = null;
            _categories = null;
            _customers = null;
            _transactions = null;
            _businessSettings = null;
            _systemPreferences = null;
            _suppliers = null;
            _expenses = null;
            _employees = null;
            _drawers = null;
            _supplierInvoices = null;
            _supplierInvoiceDetails = null;
            _inventoryHistories = null;
            _customerPayments = null;
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            await _contextLock.WaitAsync();
            try
            {
                var context = GetOrCreateContext();
                return await context.Database.BeginTransactionAsync();
            }
            catch (ObjectDisposedException)
            {
                _context = _contextFactory.CreateDbContext();
                ResetRepositories();
                return await _context.Database.BeginTransactionAsync();
            }
            finally
            {
                _contextLock.Release();
            }
        }

        public IGenericRepository<T> GetRepository<T>() where T : class
        {
            var propertyName = typeof(T).Name + "s";
            var property = GetType().GetProperties()
                .FirstOrDefault(p => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            if (property != null)
            {
                return (IGenericRepository<T>)property.GetValue(this);
            }

            return CreateRepository<T>();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _contextLock?.Dispose();
                _context?.Dispose();
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