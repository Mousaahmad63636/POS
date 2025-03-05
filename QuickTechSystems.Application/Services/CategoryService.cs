using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class CategoryService : BaseService<Category, CategoryDTO>, ICategoryService
    {
        public CategoryService(
       IUnitOfWork unitOfWork,
       IMapper mapper,
       IEventAggregator eventAggregator)
       : base(unitOfWork, mapper, unitOfWork.Categories, eventAggregator)
        {
        }

        public async Task<CategoryDTO?> GetByNameAsync(string name)
        {
            var categories = await _repository.Query()
                .Where(c => c.Name.Contains(name))
                .FirstOrDefaultAsync();
            return _mapper.Map<CategoryDTO>(categories);
        }

        public async Task<IEnumerable<CategoryDTO>> GetActiveAsync()
        {
            try
            {
                var categories = await _repository.Query()
                    .Where(c => c.IsActive)
                    .Include(c => c.Products)  // Include if you need product counts
                    .ToListAsync();
                return _mapper.Map<IEnumerable<CategoryDTO>>(categories);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetActiveAsync: {ex.Message}");
                throw;
            }
        }
        public override async Task UpdateAsync(CategoryDTO dto)
        {
            try
            {
                // Get the existing entity from the context
                var existingCategory = await _repository.GetByIdAsync(dto.CategoryId);
                if (existingCategory == null)
                {
                    throw new InvalidOperationException($"Category with ID {dto.CategoryId} not found");
                }

                // Update the existing entity properties
                _mapper.Map(dto, existingCategory);

                await _repository.UpdateAsync(existingCategory);
                await _unitOfWork.SaveChangesAsync();

                // Publish the update event
                _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Update", dto));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating category: {ex}");
                throw;
            }
        }
        public override async Task DeleteAsync(int id)
        {
            try
            {
                var category = await _unitOfWork.Context.Set<Category>()
                    .Include(c => c.Products)
                    .FirstOrDefaultAsync(c => c.CategoryId == id);

                if (category == null)
                {
                    throw new InvalidOperationException($"Category with ID {id} not found");
                }

                if (category.Products.Any())
                {
                    throw new InvalidOperationException(
                        "Cannot delete category because it has associated products. " +
                        "Please reassign or delete the products first.");
                }

                await _repository.DeleteAsync(category);
                await _unitOfWork.SaveChangesAsync();

                var categoryDto = _mapper.Map<CategoryDTO>(category);
                _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Delete", categoryDto));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting category: {ex}");
                throw;
            }
        }

    }
}