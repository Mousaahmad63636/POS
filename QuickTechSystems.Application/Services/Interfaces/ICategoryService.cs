using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ICategoryService : IBaseService<CategoryDTO>
    {
        Task<CategoryDTO?> GetByNameAsync(string name);
        Task<IEnumerable<CategoryDTO>> GetActiveAsync();
    }
}