using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IExpenseService : IBaseService<ExpenseDTO>
    {
        Task<decimal> GetTotalExpensesAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<ExpenseDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<ExpenseDTO>> GetByCategoryAsync(string category);
    }
}