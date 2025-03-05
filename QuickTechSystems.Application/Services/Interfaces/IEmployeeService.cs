using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IEmployeeService : IBaseService<EmployeeDTO>
    {
        Task<EmployeeDTO?> GetByUsernameAsync(string username);
        Task ResetPasswordAsync(int employeeId, string newPassword);
        Task UpdateEmployeeAsync(int employeeId, EmployeeDTO dto);
        Task UpdateLastLoginAsync(int employeeId);
    }
}