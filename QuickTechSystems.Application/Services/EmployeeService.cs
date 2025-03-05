using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class EmployeeService : BaseService<Employee, EmployeeDTO>, IEmployeeService
    {
        public EmployeeService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.Employees, eventAggregator)
        {
        }

        public async Task UpdateAsync(int employeeId, EmployeeDTO dto)
        {
            var existingEmployee = await _repository.GetByIdAsync(employeeId);
            if (existingEmployee == null)
                throw new InvalidOperationException($"Employee with ID {employeeId} not found");

            _mapper.Map(dto, existingEmployee);
            await _repository.UpdateAsync(existingEmployee);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<EmployeeDTO?> GetByUsernameAsync(string username)
        {
            var employee = await _repository.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Username == username);
            return _mapper.Map<EmployeeDTO>(employee);
        }

        public async Task ResetPasswordAsync(int employeeId, string newPassword)
        {
            try
            {
                Debug.WriteLine($"ResetPasswordAsync: Starting for employee ID {employeeId}");

                if (string.IsNullOrEmpty(newPassword))
                {
                    Debug.WriteLine("ResetPasswordAsync: New password is empty");
                    throw new ArgumentException("New password cannot be empty");
                }

                // Hash the password
                string hashedPassword = HashPassword(newPassword);
                Debug.WriteLine($"ResetPasswordAsync: Password hashed, length: {hashedPassword.Length}");

                // Use a direct SQL command to update the database
                string sql = "UPDATE Employees SET PasswordHash = @hash, UpdatedAt = @now WHERE EmployeeId = @id";
                var parameters = new object[] {
            new Microsoft.Data.SqlClient.SqlParameter("@hash", hashedPassword),
            new Microsoft.Data.SqlClient.SqlParameter("@now", DateTime.Now),
            new Microsoft.Data.SqlClient.SqlParameter("@id", employeeId)
        };

                // Execute the SQL and get number of affected rows
                int rowsAffected = await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, parameters);
                Debug.WriteLine($"ResetPasswordAsync: SQL executed, rows affected: {rowsAffected}");

                if (rowsAffected == 0)
                {
                    Debug.WriteLine("ResetPasswordAsync: No rows were updated");
                    throw new InvalidOperationException($"Employee with ID {employeeId} not found or update failed");
                }

                Debug.WriteLine("ResetPasswordAsync: Password updated successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetPasswordAsync: Error: {ex.Message}\nStack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task UpdateLastLoginAsync(int employeeId)
        {
            var employee = await _repository.GetByIdAsync(employeeId);
            if (employee != null)
            {
                employee.LastLogin = DateTime.Now;
                await _repository.UpdateAsync(employee);
                await _unitOfWork.SaveChangesAsync();
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}