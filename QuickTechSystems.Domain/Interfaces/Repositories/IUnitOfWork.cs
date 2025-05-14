﻿// Path: QuickTechSystems.Domain.Interfaces.Repositories/IUnitOfWork.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Domain.Interfaces.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<Product> Products { get; }
        IGenericRepository<MainStock> MainStocks { get; } // Add this line
        IGenericRepository<InventoryTransfer> InventoryTransfers { get; } // Add this line
        IGenericRepository<Category> Categories { get; }
        IGenericRepository<Customer> Customers { get; }
        IGenericRepository<Transaction> Transactions { get; }
        IGenericRepository<BusinessSetting> BusinessSettings { get; }
        IGenericRepository<SystemPreference> SystemPreferences { get; }
        IGenericRepository<Supplier> Suppliers { get; }
        IGenericRepository<Expense> Expenses { get; }
        IGenericRepository<Drawer> Drawers { get; }
        IGenericRepository<Employee> Employees { get; }
        IGenericRepository<Quote> Quotes { get; }
        IGenericRepository<RestaurantTable> RestaurantTables { get; }
        IGenericRepository<T> GetRepository<T>() where T : class;
        IGenericRepository<SupplierInvoice> SupplierInvoices { get; }
        IGenericRepository<SupplierInvoiceDetail> SupplierInvoiceDetails { get; }
        IGenericRepository<InventoryHistory> InventoryHistories { get; }
        Task<int> SaveChangesAsync();
        Task<IDbContextTransaction> BeginTransactionAsync();
        void DetachEntity<T>(T entity) where T : class;
        DbContext Context { get; }
    }
}