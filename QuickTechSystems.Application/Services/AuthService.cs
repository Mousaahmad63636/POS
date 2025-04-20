// QuickTechSystems.Application.Services/AuthService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using System.Security.Cryptography;
using System.Threading.Tasks;
using QuickTechSystems.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace QuickTechSystems.Application.Services
{
    public interface IAuthService
    {
        Task<EmployeeDTO?> LoginAsync(string username, string password);
        Task<EmployeeDTO?> LoginByCodeAsync(string secureCode);
        Task<bool> ChangePasswordAsync(int employeeId, string oldPassword, string newPassword);
    }

    public class AuthService : IAuthService
    {
        private readonly IEmployeeService _employeeService;
        private readonly IDbContextScopeService _dbContextScopeService;

        public AuthService(
            IEmployeeService employeeService,
            IDbContextScopeService dbContextScopeService)
        {
            _employeeService = employeeService;
            _dbContextScopeService = dbContextScopeService;
        }

        public async Task<EmployeeDTO?> LoginAsync(string username, string password)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var employee = await _employeeService.GetByUsernameAsync(username);
                if (employee == null) return null;

                var passwordHash = HashPassword(password);
                if (employee.PasswordHash != passwordHash) return null;

                // Ensure role is properly set
                if (employee.Username.ToLower() == "admin")
                {
                    employee.Role = "Admin";
                }

                await _employeeService.UpdateLastLoginAsync(employee.EmployeeId);
                return employee;
            });
        }

        public async Task<EmployeeDTO?> LoginByCodeAsync(string secureCode)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                if (string.IsNullOrWhiteSpace(secureCode))
                    return null;

                // Find employee by secure code
                var employees = await _employeeService.GetAllAsync();
                var employee = employees.FirstOrDefault(e => e.SecureCode == secureCode);

                if (employee == null)
                    return null;

                if (!employee.IsActive)
                    return null; // Don't allow inactive employees to login

                // Ensure role is properly set
                if (employee.Username.ToLower() == "admin")
                {
                    employee.Role = "Admin";
                }

                await _employeeService.UpdateLastLoginAsync(employee.EmployeeId);
                return employee;
            });
        }

        public async Task<bool> ChangePasswordAsync(int employeeId, string oldPassword, string newPassword)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var employee = await _employeeService.GetByIdAsync(employeeId);
                if (employee == null) return false;

                var oldHash = HashPassword(oldPassword);
                if (employee.PasswordHash != oldHash) return false;

                await _employeeService.ResetPasswordAsync(employeeId, newPassword);
                return true;
            });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}