using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;

namespace QuickTechSystems.Application.Services
{
    public class CategoryService : BaseService<Category, CategoryDTO>, ICategoryService
    {
        private readonly Dictionary<int, DateTime> _operationTimestamps;
        private readonly HashSet<int> _activeOperations;
        private readonly object _operationLock = new object();

        public CategoryService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _operationTimestamps = new Dictionary<int, DateTime>();
            _activeOperations = new HashSet<int>();
        }

        public async Task<CategoryDTO?> GetByNameAsync(string name)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var category = await _repository.Query()
                    .Where(c => c.Name.Contains(name))
                    .FirstOrDefaultAsync();

                return _mapper.Map<CategoryDTO>(category);
            }, "GetByNameAsync");
        }

        public async Task<IEnumerable<CategoryDTO>> GetByTypeAsync(string type)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var categories = await _repository.Query()
                    .Where(c => c.Type == type)
                    .Include(c => c.Products)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<CategoryDTO>>(categories);
            }, "GetByTypeAsync");
        }

        public async Task<IEnumerable<CategoryDTO>> GetActiveAsync()
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var categories = await _repository.Query()
                        .Where(c => c.IsActive)
                        .Include(c => c.Products)
                        .ToListAsync();

                    return _mapper.Map<IEnumerable<CategoryDTO>>(categories);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in GetActiveAsync: {ex.Message}");
                    throw;
                }
            }, "GetActiveAsync");
        }

        public override async Task<CategoryDTO> CreateAsync(CategoryDTO dto)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var existingCategory = await _repository.Query()
                        .FirstOrDefaultAsync(c => c.Name == dto.Name && c.Type == dto.Type);

                    if (existingCategory != null)
                    {
                        throw new InvalidOperationException($"A {dto.Type} category with the name '{dto.Name}' already exists.");
                    }

                    var entity = _mapper.Map<Category>(dto);
                    var result = await _repository.AddAsync(entity);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultDto = _mapper.Map<CategoryDTO>(result);
                    _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Create", resultDto));

                    return resultDto;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error creating category: {ex.Message}");
                    throw;
                }
            }, "CreateAsync");
        }

        public override async Task UpdateAsync(CategoryDTO dto)
        {
            lock (_operationLock)
            {
                if (_activeOperations.Contains(dto.CategoryId))
                {
                    throw new InvalidOperationException("Operation already in progress for this category");
                }
                _activeOperations.Add(dto.CategoryId);
                _operationTimestamps[dto.CategoryId] = DateTime.Now;
            }

            try
            {
                await ExecuteServiceOperationAsync(async () =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var existingCategory = await _repository.Query()
                            .FirstOrDefaultAsync(c =>
                                c.Name == dto.Name &&
                                c.Type == dto.Type &&
                                c.CategoryId != dto.CategoryId);

                        if (existingCategory != null)
                        {
                            throw new InvalidOperationException($"Another {dto.Type} category with the name '{dto.Name}' already exists.");
                        }

                        var category = await _repository.GetByIdAsync(dto.CategoryId);
                        if (category == null)
                        {
                            throw new InvalidOperationException($"Category with ID {dto.CategoryId} not found");
                        }

                        _mapper.Map(dto, category);

                        await _repository.UpdateAsync(category);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Update", dto));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error updating category: {ex}");
                        throw;
                    }
                }, "UpdateAsync");
            }
            finally
            {
                lock (_operationLock)
                {
                    _activeOperations.Remove(dto.CategoryId);
                    _operationTimestamps.Remove(dto.CategoryId);
                }
            }
        }

        public override async Task DeleteAsync(int id)
        {
            lock (_operationLock)
            {
                if (_activeOperations.Contains(id))
                {
                    throw new InvalidOperationException("Operation already in progress for this category");
                }
                _activeOperations.Add(id);
                _operationTimestamps[id] = DateTime.Now;
            }

            try
            {
                await ExecuteServiceOperationAsync(async () =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var category = await _repository.Query()
                            .Include(c => c.Products)
                            .FirstOrDefaultAsync(c => c.CategoryId == id);

                        if (category == null)
                        {
                            throw new InvalidOperationException($"Category with ID {id} not found");
                        }

                        if (category.Type == "Product" && category.Products.Any())
                        {
                            throw new InvalidOperationException(
                                "Cannot delete category because it has associated products. " +
                                "Please reassign or delete the products first.");
                        }

                        await _repository.DeleteAsync(category);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var categoryDto = _mapper.Map<CategoryDTO>(category);
                        _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Delete", categoryDto));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error deleting category: {ex}");
                        throw;
                    }
                }, "DeleteAsync");
            }
            finally
            {
                lock (_operationLock)
                {
                    _activeOperations.Remove(id);
                    _operationTimestamps.Remove(id);
                }
            }
        }

        public async Task<IEnumerable<CategoryDTO>> GetProductCategoriesAsync()
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var categories = await _repository.Query()
                        .Where(c => c.Type == "Product")
                        .ToListAsync();

                    return _mapper.Map<IEnumerable<CategoryDTO>>(categories);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in GetProductCategoriesAsync: {ex.Message}");
                    throw;
                }
            }, "GetProductCategoriesAsync");
        }

        public async Task<IEnumerable<CategoryDTO>> GetExpenseCategoriesAsync()
        {
            return await GetByTypeAsync("Expense");
        }

        public async Task<bool> IsNameUniqueWithinTypeAsync(string name, string type, int? excludeId = null)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var query = _repository.Query()
                    .Where(c => c.Name == name && c.Type == type);

                if (excludeId.HasValue)
                {
                    query = query.Where(c => c.CategoryId != excludeId.Value);
                }

                return !await query.AnyAsync();
            }, "IsNameUniqueWithinTypeAsync");
        }
    }
}