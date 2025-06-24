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
    public class ExpenseService : BaseService<Expense, ExpenseDTO>, IExpenseService
    {
        private readonly IDrawerService _drawerService;
        private readonly Dictionary<int, DateTime> _operationTimestamps;
        private readonly HashSet<int> _activeOperations;
        private readonly Dictionary<string, SemaphoreSlim> _operationLocks;
        private readonly object _operationManager = new object();

        public ExpenseService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _drawerService = drawerService;
            _operationTimestamps = new Dictionary<int, DateTime>();
            _activeOperations = new HashSet<int>();
            _operationLocks = new Dictionary<string, SemaphoreSlim>();
        }

        public async Task<decimal> GetTotalExpensesAsync(DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var expenses = await _repository.Query()
                    .Where(e => e.Date >= startDate && e.Date <= endDate)
                    .ToListAsync();

                return expenses.Sum(e => e.Amount);
            }, "GetTotalExpenses");
        }

        public async Task<IEnumerable<ExpenseDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var expenses = await _repository.Query()
                    .Where(e => e.Date >= startDate && e.Date <= endDate)
                    .OrderByDescending(e => e.Date)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<ExpenseDTO>>(expenses);
            }, "GetByDateRange");
        }

        public async Task CreateEmployeeSalaryExpenseAsync(int employeeId, decimal amount, string employeeName)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var expense = new Expense
                    {
                        Reason = $"Salary withdrawal - {employeeName}",
                        Amount = amount,
                        Date = DateTime.Now,
                        Category = "Employee Salary",
                        Notes = $"Employee ID: {employeeId}",
                        CreatedAt = DateTime.Now
                    };

                    await _repository.AddAsync(expense);
                    await _unitOfWork.SaveChangesAsync();

                    await _drawerService.ProcessExpenseAsync(
                        amount,
                        "Employee Salary",
                        $"Salary withdrawal - {employeeName}"
                    );

                    await transaction.CommitAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>(
                        "Create",
                        _mapper.Map<ExpenseDTO>(expense)
                    ));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "CreateEmployeeSalaryExpense");
        }

        public override async Task<ExpenseDTO> CreateAsync(ExpenseDTO dto)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(dto.Reason))
                    throw new InvalidOperationException("Expense reason is required");

                if (dto.Amount <= 0)
                    throw new InvalidOperationException("Amount must be greater than zero");

                var drawer = await _drawerService.GetCurrentDrawerAsync();
                if (drawer == null)
                    throw new InvalidOperationException("No active drawer found");

                if (dto.Amount > drawer.CurrentBalance)
                    throw new InvalidOperationException("Insufficient funds in drawer");

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    _unitOfWork.DetachAllEntities();

                    var entity = new Expense
                    {
                        Reason = dto.Reason,
                        Amount = dto.Amount,
                        Date = dto.Date,
                        Notes = dto.Notes,
                        Category = dto.Category,
                        IsRecurring = dto.IsRecurring,
                        CreatedAt = DateTime.Now
                    };

                    var result = await _repository.AddAsync(entity);
                    await _unitOfWork.SaveChangesAsync();

                    await _drawerService.ProcessExpenseAsync(
                        dto.Amount,
                        dto.Category,
                        dto.Reason
                    );

                    await transaction.CommitAsync();

                    _unitOfWork.DetachAllEntities();

                    var resultDto = _mapper.Map<ExpenseDTO>(result);

                    _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Create", resultDto));
                    _eventAggregator.Publish(new DrawerUpdateEvent(
                        "Expense",
                        -dto.Amount,
                        $"Expense: {dto.Category} - {dto.Reason}"
                    ));

                    return resultDto;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _unitOfWork.DetachAllEntities();
                    Debug.WriteLine($"Error creating expense: {ex.Message}");
                    throw;
                }
            }, "CreateExpense");
        }

        public override async Task UpdateAsync(ExpenseDTO dto)
        {
            if (!await AcquireEntityLock(dto.ExpenseId, "Update"))
                throw new InvalidOperationException("Operation already in progress for this expense");

            try
            {
                await ExecuteServiceOperationAsync(async () =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        _unitOfWork.DetachAllEntities();

                        var existingExpense = await _repository.GetByIdAsync(dto.ExpenseId);
                        if (existingExpense == null)
                        {
                            throw new InvalidOperationException($"Expense with ID {dto.ExpenseId} not found");
                        }

                        decimal amountDifference = dto.Amount - existingExpense.Amount;

                        if (amountDifference > 0)
                        {
                            var drawerValid = await _drawerService.ValidateTransactionAsync(amountDifference, true);
                            if (!drawerValid)
                            {
                                throw new InvalidOperationException("Insufficient funds in cash drawer for expense increase");
                            }
                        }

                        _unitOfWork.DetachEntity(existingExpense);

                        var entityToUpdate = new Expense
                        {
                            ExpenseId = dto.ExpenseId,
                            Reason = dto.Reason,
                            Amount = dto.Amount,
                            Date = dto.Date,
                            Notes = dto.Notes,
                            Category = dto.Category,
                            IsRecurring = dto.IsRecurring,
                            CreatedAt = existingExpense.CreatedAt,
                            UpdatedAt = DateTime.Now
                        };

                        await _repository.UpdateAsync(entityToUpdate);

                        if (amountDifference != 0)
                        {
                            await _drawerService.ProcessExpenseAsync(
                                amountDifference,
                                dto.Category,
                                $"Expense adjustment: {dto.Reason}"
                            );
                        }

                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _unitOfWork.DetachAllEntities();

                        _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Update", dto));
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        _unitOfWork.DetachAllEntities();
                        throw;
                    }
                }, $"UpdateExpense_{dto.ExpenseId}");
            }
            finally
            {
                ReleaseEntityLock(dto.ExpenseId, "Update");
            }
        }

        public override async Task DeleteAsync(int id)
        {
            if (!await AcquireEntityLock(id, "Delete"))
                throw new InvalidOperationException("Operation already in progress for this expense");

            try
            {
                await ExecuteServiceOperationAsync(async () =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        _unitOfWork.DetachAllEntities();

                        var expense = await _repository.GetByIdAsync(id);
                        if (expense == null)
                            throw new InvalidOperationException($"Expense with ID {id} not found");

                        var expenseDto = _mapper.Map<ExpenseDTO>(expense);

                        await _drawerService.AddCashTransactionAsync(expense.Amount, true);

                        await _repository.DeleteAsync(expense);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        _unitOfWork.DetachAllEntities();

                        _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Delete", expenseDto));
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        _unitOfWork.DetachAllEntities();
                        throw;
                    }
                }, $"DeleteExpense_{id}");
            }
            finally
            {
                ReleaseEntityLock(id, "Delete");
            }
        }

        public async Task<IEnumerable<ExpenseDTO>> GetByCategoryAsync(string category)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var expenses = await _repository.Query()
                    .Where(e => e.Category == category)
                    .OrderByDescending(e => e.Date)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<ExpenseDTO>>(expenses);
            }, "GetByCategory");
        }

        public async Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                return await _repository.Query()
                    .Where(e => e.Date >= startDate && e.Date <= endDate)
                    .GroupBy(e => e.Category)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Sum(e => e.Amount)
                    );
            }, "GetCategoryTotals");
        }

        public async Task<bool> ValidateExpenseAsync(decimal amount)
        {
            return await _drawerService.ValidateTransactionAsync(amount, true);
        }

        private async Task<bool> AcquireEntityLock(int entityId, string operationType)
        {
            var lockKey = $"{operationType}_{entityId}";

            lock (_operationManager)
            {
                if (_activeOperations.Contains(entityId))
                    return false;

                _activeOperations.Add(entityId);
                _operationTimestamps[entityId] = DateTime.Now;

                if (!_operationLocks.ContainsKey(lockKey))
                    _operationLocks[lockKey] = new SemaphoreSlim(1, 1);
            }

            return await _operationLocks[lockKey].WaitAsync(1000);
        }

        private void ReleaseEntityLock(int entityId, string operationType)
        {
            var lockKey = $"{operationType}_{entityId}";

            lock (_operationManager)
            {
                _activeOperations.Remove(entityId);
                _operationTimestamps.Remove(entityId);

                if (_operationLocks.ContainsKey(lockKey))
                {
                    _operationLocks[lockKey].Release();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_operationManager)
                {
                    foreach (var lockPair in _operationLocks)
                    {
                        lockPair.Value?.Dispose();
                    }
                    _operationLocks.Clear();
                    _activeOperations.Clear();
                    _operationTimestamps.Clear();
                }
            }
            base.Dispose(disposing);
        }
    }
}