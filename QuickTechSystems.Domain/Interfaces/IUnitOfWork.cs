﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Domain.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<Product> Products { get; }
        IGenericRepository<Category> Categories { get; }
        IGenericRepository<Customer> Customers { get; }
        IGenericRepository<Transaction> Transactions { get; }
        IGenericRepository<BusinessSetting> BusinessSettings { get; }
        IGenericRepository<SystemPreference> SystemPreferences { get; }
        IGenericRepository<Supplier> Suppliers { get; }
        IGenericRepository<Expense> Expenses { get; }
        IGenericRepository<Drawer> Drawers { get; }
        IGenericRepository<Employee> Employees { get; }
        IGenericRepository<RestaurantTable> RestaurantTables { get; }
        IGenericRepository<SupplierInvoice> SupplierInvoices { get; }
        IGenericRepository<SupplierInvoiceDetail> SupplierInvoiceDetails { get; }
        IGenericRepository<InventoryHistory> InventoryHistories { get; }
        IGenericRepository<CustomerPayment> CustomerPayments { get; }

        IGenericRepository<T> GetRepository<T>() where T : class;
        Task<int> SaveChangesAsync();
        Task<IDbContextTransaction> BeginTransactionAsync();
        void DetachEntity<T>(T entity) where T : class;
        void DetachAllEntities();
        DbContext Context { get; }
    }
}