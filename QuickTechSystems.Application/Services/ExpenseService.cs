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

        public ExpenseService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _drawerService = drawerService;
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
                ValidateExpense(dto);

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
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

                    var resultDto = _mapper.Map<ExpenseDTO>(result);
                    _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Create", resultDto));

                    return resultDto;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "CreateExpense");
        }

        public override async Task UpdateAsync(ExpenseDTO dto)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                ValidateExpense(dto);

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var existingExpense = await _repository.GetByIdAsync(dto.ExpenseId);
                    if (existingExpense == null)
                        throw new InvalidOperationException($"Expense with ID {dto.ExpenseId} not found");

                    decimal amountDifference = dto.Amount - existingExpense.Amount;

                    existingExpense.Reason = dto.Reason;
                    existingExpense.Amount = dto.Amount;
                    existingExpense.Date = dto.Date;
                    existingExpense.Notes = dto.Notes;
                    existingExpense.Category = dto.Category;
                    existingExpense.IsRecurring = dto.IsRecurring;
                    existingExpense.UpdatedAt = DateTime.Now;

                    await _repository.UpdateAsync(existingExpense);

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

                    _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Update", dto));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "UpdateExpense");
        }

        public override async Task DeleteAsync(int id)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var expense = await _repository.GetByIdAsync(id);
                    if (expense == null)
                        throw new InvalidOperationException($"Expense with ID {id} not found");

                    var expenseDto = _mapper.Map<ExpenseDTO>(expense);

                    await _drawerService.AddCashTransactionAsync(expense.Amount, true);
                    await _repository.DeleteAsync(expense);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Delete", expenseDto));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "DeleteExpense");
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

        private static void ValidateExpense(ExpenseDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new ArgumentException("Expense reason is required");

            if (dto.Amount <= 0)
                throw new ArgumentException("Amount must be greater than zero");

            if (string.IsNullOrWhiteSpace(dto.Category))
                throw new ArgumentException("Category is required");
        }
    }
}