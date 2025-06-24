using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace QuickTechSystems.Infrastructure.Data
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected ApplicationDbContext _context;
        protected readonly DbSet<T> _dbSet;
        protected readonly IDbContextFactory<ApplicationDbContext>? _contextFactory;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        public GenericRepository(ApplicationDbContext context, IDbContextFactory<ApplicationDbContext>? contextFactory = null)
        {
            _context = context;
            _contextFactory = contextFactory;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(int id)
        {
            await _operationLock.WaitAsync();
            try
            {
                return await ExecuteWithFreshContextAsync(async ctx => await ctx.Set<T>().FindAsync(id));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            await _operationLock.WaitAsync();
            try
            {
                return await ExecuteWithFreshContextAsync(async ctx => await ctx.Set<T>().ToListAsync());
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual async Task<T> AddAsync(T entity)
        {
            await _operationLock.WaitAsync();
            try
            {
                return await ExecuteWithCurrentContextAsync(async () =>
                {
                    await _dbSet.AddAsync(entity);
                    return entity;
                });
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual async Task UpdateAsync(T entity)
        {
            await _operationLock.WaitAsync();
            try
            {
                await ExecuteWithCurrentContextAsync(async () =>
                {
                    _context.Entry(entity).State = EntityState.Modified;
                    return Task.CompletedTask;
                });
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual async Task DeleteAsync(T entity)
        {
            await _operationLock.WaitAsync();
            try
            {
                await ExecuteWithCurrentContextAsync(async () =>
                {
                    _dbSet.Remove(entity);
                    return Task.CompletedTask;
                });
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual IQueryable<T> Query()
        {
            try
            {
                return _dbSet.AsQueryable();
            }
            catch (ObjectDisposedException)
            {
                if (_contextFactory != null)
                {
                    RefreshContext();
                    return _context.Set<T>().AsQueryable();
                }
                throw;
            }
        }

        private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(Func<ApplicationDbContext, Task<TResult>> operation)
        {
            if (_contextFactory == null)
            {
                return await operation(_context);
            }

            using var freshContext = _contextFactory.CreateDbContext();
            return await operation(freshContext);
        }

        private async Task<TResult> ExecuteWithCurrentContextAsync<TResult>(Func<Task<TResult>> operation)
        {
            try
            {
                return await operation();
            }
            catch (ObjectDisposedException)
            {
                if (_contextFactory != null)
                {
                    RefreshContext();
                    return await operation();
                }
                throw;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("disposed") || ex.Message.Contains("second operation"))
            {
                if (_contextFactory != null)
                {
                    await Task.Delay(50);
                    RefreshContext();
                    return await operation();
                }
                throw;
            }
        }

        private void RefreshContext()
        {
            if (_contextFactory != null)
            {
                _context?.Dispose();
                _context = _contextFactory.CreateDbContext();
            }
        }
    }
}