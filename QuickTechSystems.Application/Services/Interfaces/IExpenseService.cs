using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    /// <summary>
    /// Service interface for expense management operations
    /// </summary>
    public interface IExpenseService : IBaseService<ExpenseDTO>
    {
        /// <summary>
        /// Gets the total expense amount for a date range
        /// </summary>
        /// <param name="startDate">Start date for filtering</param>
        /// <param name="endDate">End date for filtering</param>
        /// <returns>Total expense amount</returns>
        Task<decimal> GetTotalExpensesAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Gets expenses within a date range
        /// </summary>
        /// <param name="startDate">Start date for filtering</param>
        /// <param name="endDate">End date for filtering</param>
        /// <returns>Collection of expenses</returns>
        Task<IEnumerable<ExpenseDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Gets expenses by category
        /// </summary>
        /// <param name="category">Category to filter by</param>
        /// <returns>Collection of expenses</returns>
        Task<IEnumerable<ExpenseDTO>> GetByCategoryAsync(string category);

        /// <summary>
        /// Validates if an expense amount can be processed
        /// </summary>
        /// <param name="amount">Amount to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        Task<bool> ValidateExpenseAsync(decimal amount);

        /// <summary>
        /// Creates an expense for employee salary withdrawal
        /// </summary>
        /// <param name="employeeId">Employee ID</param>
        /// <param name="amount">Salary amount</param>
        /// <param name="employeeName">Employee name</param>
        Task CreateEmployeeSalaryExpenseAsync(int employeeId, decimal amount, string employeeName);

        /// <summary>
        /// Gets category totals for a date range
        /// </summary>
        /// <param name="startDate">Start date for filtering</param>
        /// <param name="endDate">End date for filtering</param>
        /// <returns>Dictionary of category names and totals</returns>
        Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Gets a paginated list of expenses with optional filtering
        /// </summary>
        /// <param name="pageNumber">Page number (1-based)</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <param name="category">Optional category filter</param>
        /// <param name="startDate">Optional start date filter</param>
        /// <param name="endDate">Optional end date filter</param>
        /// <returns>Paginated result containing expenses</returns>
        Task<PagedResult<ExpenseDTO>> GetPagedAsync(int pageNumber, int pageSize, string? category = null, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// Gets the total count of expenses matching the specified filters
        /// </summary>
        /// <param name="category">Optional category filter</param>
        /// <param name="startDate">Optional start date filter</param>
        /// <param name="endDate">Optional end date filter</param>
        /// <returns>Total count of matching expenses</returns>
        Task<int> GetTotalCountAsync(string? category = null, DateTime? startDate = null, DateTime? endDate = null);
    }
}