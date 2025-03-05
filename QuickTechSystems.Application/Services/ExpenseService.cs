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
    public class ExpenseService : BaseService<Expense, ExpenseDTO>, IExpenseService
    {
        public ExpenseService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.Expenses, eventAggregator)
        {
        }

        public async Task<decimal> GetTotalExpensesAsync(DateTime startDate, DateTime endDate)
        {
            var expenses = await _repository.Query()
                .Where(e => e.Date >= startDate && e.Date <= endDate)
                .ToListAsync();

            return expenses.Sum(e => e.Amount);
        }

        public async Task<IEnumerable<ExpenseDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var expenses = await _repository.Query()
                .Where(e => e.Date >= startDate && e.Date <= endDate)
                .OrderByDescending(e => e.Date)
                .ToListAsync();

            return _mapper.Map<IEnumerable<ExpenseDTO>>(expenses);
        }
        public override async Task UpdateAsync(ExpenseDTO dto)
        {
            try
            {
                // Get the existing entity from the context
                var existingExpense = await _repository.GetByIdAsync(dto.ExpenseId);
                if (existingExpense == null)
                {
                    throw new InvalidOperationException($"Expense with ID {dto.ExpenseId} not found");
                }

                // Update the existing entity properties
                _mapper.Map(dto, existingExpense);

                await _repository.UpdateAsync(existingExpense);
                await _unitOfWork.SaveChangesAsync();

                // Publish the update event
                _eventAggregator.Publish(new EntityChangedEvent<ExpenseDTO>("Update", dto));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating expense: {ex}");
                throw;
            }
        }
        public async Task<IEnumerable<ExpenseDTO>> GetByCategoryAsync(string category)
        {
            var expenses = await _repository.Query()
                .Where(e => e.Category == category)
                .OrderByDescending(e => e.Date)
                .ToListAsync();

            return _mapper.Map<IEnumerable<ExpenseDTO>>(expenses);
        }
    }
}