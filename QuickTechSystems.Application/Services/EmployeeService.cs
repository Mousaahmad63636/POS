using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
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

        public async Task UpdateEmployeeAsync(int employeeId, EmployeeDTO dto)
        {
            var existingEmployee = await _repository.GetByIdAsync(employeeId);
            if (existingEmployee == null)
                throw new InvalidOperationException($"Employee with ID {employeeId} not found");

            _mapper.Map(dto, existingEmployee);
            await _repository.UpdateAsync(existingEmployee);
            await _unitOfWork.SaveChangesAsync();
            _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>("Update", dto));
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
                Debug.WriteLine($"ResetPasswordAsync: Password hashed: {hashedPassword}");
                Debug.WriteLine($"ResetPasswordAsync: Hashed password length: {hashedPassword.Length}");

                // Use direct SQL for updating the password
                string sql = "UPDATE Employees SET PasswordHash = @hash WHERE EmployeeId = @id";

                var hashParam = new Microsoft.Data.SqlClient.SqlParameter("@hash", hashedPassword);
                var idParam = new Microsoft.Data.SqlClient.SqlParameter("@id", employeeId);

                Debug.WriteLine($"ResetPasswordAsync: SQL query: {sql}");
                Debug.WriteLine($"ResetPasswordAsync: Parameters - @hash: [{hashedPassword.Substring(0, 5)}...], @id: {employeeId}");

                // Execute SQL with explicit parameter creation
                int rowsAffected = await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, hashParam, idParam);

                Debug.WriteLine($"ResetPasswordAsync: SQL executed, rows affected: {rowsAffected}");

                if (rowsAffected == 0)
                {
                    Debug.WriteLine("ResetPasswordAsync: No rows were updated");
                    throw new InvalidOperationException($"Employee with ID {employeeId} not found or update failed");
                }

                // Verify the update in the database
                var updatedEmployee = await _repository.GetByIdAsync(employeeId);
                if (updatedEmployee != null)
                {
                    Debug.WriteLine($"ResetPasswordAsync: Verification - Employee found with ID {employeeId}");
                    Debug.WriteLine($"ResetPasswordAsync: Verification - PasswordHash: [{updatedEmployee.PasswordHash.Substring(0, 5)}...]");

                    // Check if the hashes match
                    if (updatedEmployee.PasswordHash == hashedPassword)
                    {
                        Debug.WriteLine("ResetPasswordAsync: Verification - Password hashes match");
                    }
                    else
                    {
                        Debug.WriteLine("ResetPasswordAsync: Verification - Password hashes DO NOT match!");
                    }
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetPasswordAsync: Error: {ex.Message}");
                Debug.WriteLine($"ResetPasswordAsync: Stack trace: {ex.StackTrace}");
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