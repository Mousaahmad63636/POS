using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using System.Security.Cryptography;
using System.Text;
using QuickTechSystems.Domain.Interfaces;
using QuickTechSystems.Application.Mappings;

namespace QuickTechSystems.Application.Services
{
    public class EmployeeService : BaseService<Employee, EmployeeDTO>, IEmployeeService, IDisposable
    {
        private readonly IDrawerService _drawerService;
        private readonly IExpenseService _expenseService;
        private readonly Dictionary<int, DateTime> _lastOperationTimes = new Dictionary<int, DateTime>();
        private readonly Dictionary<string, SemaphoreSlim> _employeeOperationLocks = new Dictionary<string, SemaphoreSlim>();

        public EmployeeService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IExpenseService expenseService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _drawerService = drawerService;
            _expenseService = expenseService;
        }

        public async Task<bool> ProcessSalaryWithdrawalAsync(int employeeId, decimal amount, string notes)
        {
            var operationKey = $"withdrawal_{employeeId}";
            var operationLock = GetOrCreateEmployeeOperationLock(operationKey);

            if (!await operationLock.WaitAsync(100))
            {
                return false;
            }

            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var employeeEntity = await _repository.Query()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                        if (employeeEntity == null || amount > employeeEntity.CurrentBalance)
                            return false;

                        await _expenseService.CreateEmployeeSalaryExpenseAsync(
                            employeeId,
                            amount,
                            $"{employeeEntity.FirstName} {employeeEntity.LastName}"
                        );

                        var updatedEmployee = new Employee
                        {
                            EmployeeId = employeeEntity.EmployeeId,
                            Username = employeeEntity.Username,
                            PasswordHash = employeeEntity.PasswordHash,
                            FirstName = employeeEntity.FirstName,
                            LastName = employeeEntity.LastName,
                            Role = employeeEntity.Role,
                            IsActive = employeeEntity.IsActive,
                            CreatedAt = employeeEntity.CreatedAt,
                            LastLogin = employeeEntity.LastLogin,
                            MonthlySalary = employeeEntity.MonthlySalary,
                            CurrentBalance = employeeEntity.CurrentBalance - amount
                        };

                        var salaryTransaction = new EmployeeSalaryTransaction
                        {
                            EmployeeId = employeeId,
                            Amount = -amount,
                            TransactionType = "Withdrawal",
                            TransactionDate = DateTime.Now,
                            Notes = notes
                        };

                        _unitOfWork.Context.Entry(updatedEmployee).State = EntityState.Modified;
                        await _unitOfWork.Context.Set<EmployeeSalaryTransaction>().AddAsync(salaryTransaction);

                        await _unitOfWork.SaveChangesAsync();

                        _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>(
                            "Update",
                            _mapper.Map<EmployeeDTO>(updatedEmployee)
                        ));

                        await transaction.CommitAsync();

                        _lastOperationTimes[employeeId] = DateTime.Now;
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }
            finally
            {
                operationLock.Release();
            }
        }

        public async Task ProcessMonthlySalaryAsync(int employeeId)
        {
            var operationKey = $"salary_{employeeId}";
            var operationLock = GetOrCreateEmployeeOperationLock(operationKey);

            if (!await operationLock.WaitAsync(100))
            {
                throw new InvalidOperationException("Another salary operation is in progress for this employee");
            }

            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var employeeEntity = await _repository.Query()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                        if (employeeEntity == null)
                            throw new InvalidOperationException("Employee not found");

                        var updatedEmployee = new Employee
                        {
                            EmployeeId = employeeEntity.EmployeeId,
                            Username = employeeEntity.Username,
                            PasswordHash = employeeEntity.PasswordHash,
                            FirstName = employeeEntity.FirstName,
                            LastName = employeeEntity.LastName,
                            Role = employeeEntity.Role,
                            IsActive = employeeEntity.IsActive,
                            CreatedAt = employeeEntity.CreatedAt,
                            LastLogin = employeeEntity.LastLogin,
                            MonthlySalary = employeeEntity.MonthlySalary,
                            CurrentBalance = employeeEntity.CurrentBalance + employeeEntity.MonthlySalary
                        };

                        var salaryTransaction = new EmployeeSalaryTransaction
                        {
                            EmployeeId = employeeId,
                            Amount = employeeEntity.MonthlySalary,
                            TransactionType = "Salary",
                            TransactionDate = DateTime.Now,
                            Notes = "Monthly salary payment"
                        };

                        _unitOfWork.Context.Entry(updatedEmployee).State = EntityState.Modified;
                        await _unitOfWork.Context.Set<EmployeeSalaryTransaction>().AddAsync(salaryTransaction);

                        await _unitOfWork.SaveChangesAsync();

                        _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>(
                            "Update",
                            _mapper.Map<EmployeeDTO>(updatedEmployee)
                        ));

                        await transaction.CommitAsync();

                        _lastOperationTimes[employeeId] = DateTime.Now;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }
            finally
            {
                operationLock.Release();
            }
        }

        public async Task<IEnumerable<EmployeeSalaryTransactionDTO>> GetSalaryHistoryAsync(int employeeId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var transactions = await _unitOfWork.Context.Set<EmployeeSalaryTransaction>()
                    .AsNoTracking()
                    .Include(t => t.Employee)
                    .Where(t => t.EmployeeId == employeeId)
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<EmployeeSalaryTransactionDTO>>(transactions);
            });
        }

        public async Task UpdateAsync(int employeeId, EmployeeDTO dto)
        {
            var operationKey = $"update_{employeeId}";
            var operationLock = GetOrCreateEmployeeOperationLock(operationKey);

            if (!await operationLock.WaitAsync(100))
            {
                throw new InvalidOperationException("Another update operation is in progress for this employee");
            }

            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    var existingEmployee = await _repository.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                    if (existingEmployee == null)
                        throw new InvalidOperationException($"Employee with ID {employeeId} not found");

                    var updatedEmployee = _mapper.Map<Employee>(dto);
                    updatedEmployee.EmployeeId = employeeId;
                    updatedEmployee.CreatedAt = existingEmployee.CreatedAt;
                    updatedEmployee.LastLogin = existingEmployee.LastLogin;

                    if (string.IsNullOrEmpty(updatedEmployee.PasswordHash))
                    {
                        updatedEmployee.PasswordHash = existingEmployee.PasswordHash;
                    }

                    _unitOfWork.Context.Entry(updatedEmployee).State = EntityState.Modified;
                    await _unitOfWork.SaveChangesAsync();
                });
            }
            finally
            {
                operationLock.Release();
            }
        }

        public async Task<EmployeeDTO?> GetByUsernameAsync(string username)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var employee = await _repository.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Username == username);
                return _mapper.Map<EmployeeDTO>(employee);
            });
        }

        public async Task ResetPasswordAsync(int employeeId, string newPassword)
        {
            var operationKey = $"password_{employeeId}";
            var operationLock = GetOrCreateEmployeeOperationLock(operationKey);

            if (!await operationLock.WaitAsync(100))
            {
                throw new InvalidOperationException("Another password operation is in progress for this employee");
            }

            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    var existingEmployee = await _repository.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                    if (existingEmployee == null)
                        throw new InvalidOperationException("Employee not found");

                    var updatedEmployee = new Employee
                    {
                        EmployeeId = existingEmployee.EmployeeId,
                        Username = existingEmployee.Username,
                        PasswordHash = HashPassword(newPassword),
                        FirstName = existingEmployee.FirstName,
                        LastName = existingEmployee.LastName,
                        Role = existingEmployee.Role,
                        IsActive = existingEmployee.IsActive,
                        CreatedAt = existingEmployee.CreatedAt,
                        LastLogin = existingEmployee.LastLogin,
                        MonthlySalary = existingEmployee.MonthlySalary,
                        CurrentBalance = existingEmployee.CurrentBalance
                    };

                    _unitOfWork.Context.Entry(updatedEmployee).State = EntityState.Modified;
                    await _unitOfWork.SaveChangesAsync();
                });
            }
            finally
            {
                operationLock.Release();
            }
        }

        public async Task UpdateLastLoginAsync(int employeeId)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var existingEmployee = await _repository.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

                if (existingEmployee != null)
                {
                    var updatedEmployee = new Employee
                    {
                        EmployeeId = existingEmployee.EmployeeId,
                        Username = existingEmployee.Username,
                        PasswordHash = existingEmployee.PasswordHash,
                        FirstName = existingEmployee.FirstName,
                        LastName = existingEmployee.LastName,
                        Role = existingEmployee.Role,
                        IsActive = existingEmployee.IsActive,
                        CreatedAt = existingEmployee.CreatedAt,
                        LastLogin = DateTime.Now,
                        MonthlySalary = existingEmployee.MonthlySalary,
                        CurrentBalance = existingEmployee.CurrentBalance
                    };

                    _unitOfWork.Context.Entry(updatedEmployee).State = EntityState.Modified;
                    await _unitOfWork.SaveChangesAsync();
                }
            });
        }

        public override async Task<EmployeeDTO> CreateAsync(EmployeeDTO dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var existingEmployee = await GetByUsernameAsync(dto.Username);
                if (existingEmployee != null)
                {
                    throw new InvalidOperationException("Username already exists");
                }

                var entity = _mapper.Map<Employee>(dto);
                entity.CreatedAt = DateTime.Now;

                var result = await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                var resultDto = _mapper.Map<EmployeeDTO>(result);
                _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>("Create", resultDto));

                return resultDto;
            });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        public override async Task<IEnumerable<EmployeeDTO>> GetAllAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var employees = await _repository.Query()
                    .AsNoTracking()
                    .Include(e => e.SalaryTransactions)
                    .OrderBy(e => e.FirstName)
                    .ToListAsync();
                return _mapper.Map<IEnumerable<EmployeeDTO>>(employees);
            });
        }

        public override async Task<EmployeeDTO?> GetByIdAsync(int id)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var employee = await _repository.Query()
                    .AsNoTracking()
                    .Include(e => e.SalaryTransactions)
                    .FirstOrDefaultAsync(e => e.EmployeeId == id);
                return _mapper.Map<EmployeeDTO>(employee);
            });
        }

        private SemaphoreSlim GetOrCreateEmployeeOperationLock(string operationKey)
        {
            lock (_employeeOperationLocks)
            {
                if (!_employeeOperationLocks.ContainsKey(operationKey))
                {
                    _employeeOperationLocks[operationKey] = new SemaphoreSlim(1, 1);
                }
                return _employeeOperationLocks[operationKey];
            }
        }

        private void CleanupOldOperationLocks()
        {
            lock (_employeeOperationLocks)
            {
                var keysToRemove = new List<string>();
                var cutoffTime = DateTime.Now.AddMinutes(-5);

                foreach (var kvp in _lastOperationTimes)
                {
                    if (kvp.Value < cutoffTime)
                    {
                        var operationKeys = _employeeOperationLocks.Keys
                            .Where(k => k.EndsWith($"_{kvp.Key}"))
                            .ToList();

                        foreach (var key in operationKeys)
                        {
                            if (_employeeOperationLocks[key].CurrentCount == 1)
                            {
                                _employeeOperationLocks[key].Dispose();
                                keysToRemove.Add(key);
                            }
                        }
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _employeeOperationLocks.Remove(key);
                }
            }
        }

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                lock (_employeeOperationLocks)
                {
                    foreach (var semaphore in _employeeOperationLocks.Values)
                    {
                        semaphore?.Dispose();
                    }
                    _employeeOperationLocks.Clear();
                }
                _lastOperationTimes.Clear();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}