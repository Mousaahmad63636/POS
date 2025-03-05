using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Infrastructure.Data;

namespace QuickTechSystems.Infrastructure.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly ApplicationDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public GenericRepository(ApplicationDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public virtual async Task<T> AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
            return entity;
        }

        public virtual Task UpdateAsync(T entity)
        {
            // First check if the entity is already being tracked
            var entry = _context.Entry(entity);

            if (entry.State == EntityState.Detached)
            {
                // If entity is detached, try to find the entity by key values
                var keyValues = _context.Model.FindEntityType(typeof(T))
                    .FindPrimaryKey().Properties
                    .Select(p => entry.Property(p.Name).CurrentValue)
                    .ToArray();

                // Look for the entity with these key values
                var attachedEntity = _dbSet.Find(keyValues);

                // If entity exists in context, detach it first
                if (attachedEntity != null)
                {
                    _context.Entry(attachedEntity).State = EntityState.Detached;
                }

                // Now set the incoming entity to Modified state
                _context.Entry(entity).State = EntityState.Modified;
            }
            else
            {
                // If the entity is already being tracked, just mark it as modified
                entry.State = EntityState.Modified;
            }

            return Task.CompletedTask;
        }

        public virtual Task DeleteAsync(T entity)
        {
            _dbSet.Remove(entity);
            return Task.CompletedTask;
        }

        public virtual IQueryable<T> Query()
        {
            return _dbSet.AsQueryable();
        }
    }
}