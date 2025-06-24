using System.Diagnostics;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;
using Microsoft.Data.SqlClient;

namespace QuickTechSystems.Application.Services
{
    public class TransactionService : BaseService<Transaction, TransactionDTO>, ITransactionService
    {
        private readonly Dictionary<int, SemaphoreSlim> _transactionLocks;
        private readonly SemaphoreSlim _lockManagerSemaphore;
        private readonly Dictionary<int, SemaphoreSlim> _productStockLocks;
        private readonly SemaphoreSlim _queryLock;

        public TransactionService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _transactionLocks = new Dictionary<int, SemaphoreSlim>();
            _lockManagerSemaphore = new SemaphoreSlim(1, 1);
            _productStockLocks = new Dictionary<int, SemaphoreSlim>();
            _queryLock = new SemaphoreSlim(1, 1);
        }

        private async Task<SemaphoreSlim> GetTransactionLock(int transactionId)
        {
            await _lockManagerSemaphore.WaitAsync();
            try
            {
                if (!_transactionLocks.ContainsKey(transactionId))
                {
                    _transactionLocks[transactionId] = new SemaphoreSlim(1, 1);
                }
                return _transactionLocks[transactionId];
            }
            finally
            {
                _lockManagerSemaphore.Release();
            }
        }

        private async Task<SemaphoreSlim> GetProductStockLock(int productId)
        {
            await _lockManagerSemaphore.WaitAsync();
            try
            {
                if (!_productStockLocks.ContainsKey(productId))
                {
                    _productStockLocks[productId] = new SemaphoreSlim(1, 1);
                }
                return _productStockLocks[productId];
            }
            finally
            {
                _lockManagerSemaphore.Release();
            }
        }

        public override async Task<IEnumerable<TransactionDTO>> GetAllAsync()
        {
            await _queryLock.WaitAsync();
            try
            {
                Debug.WriteLine("TransactionService.GetAllAsync - Starting query");

                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var query = _repository.Query()
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                                    .ThenInclude(p => p.Category)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking();

                        Debug.WriteLine($"Query created, executing ToListAsync...");
                        var transactions = await query.ToListAsync();
                        Debug.WriteLine($"Found {transactions.Count} transactions in database");

                        foreach (var transaction in transactions)
                        {
                            Debug.WriteLine($"Transaction {transaction.TransactionId}: {transaction.CustomerName}, Date: {transaction.TransactionDate}, Amount: {transaction.TotalAmount}");
                        }

                        var dtos = _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                        Debug.WriteLine($"Mapped to {dtos.Count()} DTOs");
                        return dtos;
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    var query = _repository.Query()
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                                .ThenInclude(p => p.Category)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking();

                    return await query.ToListAsync();
                });

                Debug.WriteLine($"Fallback method found {fallbackTransactions.Count} transactions");
                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetAllAsync: {ex}");
                throw;
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public override async Task<TransactionDTO?> GetByIdAsync(int id)
        {
            return await GetTransactionWithDetailsAsync(id);
        }

        public async Task<TransactionDTO?> GetTransactionWithDetailsAsync(int transactionId)
        {
            await _queryLock.WaitAsync();
            try
            {
                Debug.WriteLine($"Getting transaction {transactionId} with details");

                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transaction = await _repository.Query()
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                                    .ThenInclude(p => p.Category)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                                    .ThenInclude(p => p.Supplier)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                        return _mapper.Map<TransactionDTO>(transaction);
                    });
                }

                var fallbackTransaction = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                                .ThenInclude(p => p.Category)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                                .ThenInclude(p => p.Supplier)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
                });

                return _mapper.Map<TransactionDTO>(fallbackTransaction);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            await _queryLock.WaitAsync();
            try
            {
                Debug.WriteLine($"Getting transactions between {startDate:yyyy-MM-dd} and {endDate:yyyy-MM-dd}");

                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transactions = await _repository.Query()
                            .Where(t => t.TransactionDate.Date >= startDate.Date && t.TransactionDate.Date <= endDate.Date)
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();

                        Debug.WriteLine($"Found {transactions.Count} transactions in date range");
                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Where(t => t.TransactionDate.Date >= startDate.Date && t.TransactionDate.Date <= endDate.Date)
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetTransactionsByEmployeeAsync(string cashierId)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transactions = await _repository.Query()
                            .Where(t => t.CashierId == cashierId)
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();
                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Where(t => t.CashierId == cashierId)
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetTransactionsByTypeAsync(TransactionType transactionType)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transactions = await _repository.Query()
                            .Where(t => t.TransactionType == transactionType)
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();
                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Where(t => t.TransactionType == transactionType)
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetTransactionsByStatusAsync(TransactionStatus status)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transactions = await _repository.Query()
                            .Where(t => t.Status == status)
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();
                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Where(t => t.Status == status)
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> SearchTransactionsAsync(string searchTerm)
        {
            await _queryLock.WaitAsync();
            try
            {
                var lowercaseSearch = searchTerm.ToLower();

                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var transactions = await _repository.Query()
                            .Where(t =>
                                t.CustomerName.ToLower().Contains(lowercaseSearch) ||
                                t.CashierName.ToLower().Contains(lowercaseSearch) ||
                                t.PaymentMethod.ToLower().Contains(lowercaseSearch) ||
                                t.TransactionId.ToString().Contains(searchTerm))
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();
                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    return await _repository.Query()
                        .Where(t =>
                            t.CustomerName.ToLower().Contains(lowercaseSearch) ||
                            t.CashierName.ToLower().Contains(lowercaseSearch) ||
                            t.PaymentMethod.ToLower().Contains(lowercaseSearch) ||
                            t.TransactionId.ToString().Contains(searchTerm))
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetFilteredTransactionsAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? cashierId = null,
            TransactionType? transactionType = null,
            TransactionStatus? status = null,
            string? searchTerm = null)
        {
            await _queryLock.WaitAsync();
            try
            {
                Debug.WriteLine($"GetFilteredTransactionsAsync called with:");
                Debug.WriteLine($"  StartDate: {startDate?.ToString("yyyy-MM-dd")}");
                Debug.WriteLine($"  EndDate: {endDate?.ToString("yyyy-MM-dd")}");
                Debug.WriteLine($"  CashierId: {cashierId}");
                Debug.WriteLine($"  TransactionType: {transactionType}");
                Debug.WriteLine($"  Status: {status}");
                Debug.WriteLine($"  SearchTerm: {searchTerm}");

                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var query = _repository.Query().AsQueryable();

                        if (startDate.HasValue)
                        {
                            query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);
                            Debug.WriteLine($"Applied start date filter: {startDate.Value.Date:yyyy-MM-dd}");
                        }

                        if (endDate.HasValue)
                        {
                            query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);
                            Debug.WriteLine($"Applied end date filter: {endDate.Value.Date:yyyy-MM-dd}");
                        }

                        if (!string.IsNullOrEmpty(cashierId))
                        {
                            query = query.Where(t => t.CashierId == cashierId);
                            Debug.WriteLine($"Applied cashier filter: {cashierId}");
                        }

                        if (transactionType.HasValue)
                        {
                            query = query.Where(t => t.TransactionType == transactionType.Value);
                            Debug.WriteLine($"Applied transaction type filter: {transactionType}");
                        }

                        if (status.HasValue)
                        {
                            query = query.Where(t => t.Status == status.Value);
                            Debug.WriteLine($"Applied status filter: {status}");
                        }

                        if (!string.IsNullOrEmpty(searchTerm))
                        {
                            var lowercaseSearch = searchTerm.ToLower();
                            query = query.Where(t =>
                                t.CustomerName.ToLower().Contains(lowercaseSearch) ||
                                t.CashierName.ToLower().Contains(lowercaseSearch) ||
                                t.PaymentMethod.ToLower().Contains(lowercaseSearch) ||
                                t.TransactionId.ToString().Contains(searchTerm));
                            Debug.WriteLine($"Applied search filter: {searchTerm}");
                        }

                        var transactions = await query
                            .Include(t => t.Customer)
                            .Include(t => t.TransactionDetails)
                                .ThenInclude(td => td.Product)
                            .OrderByDescending(t => t.TransactionDate)
                            .AsNoTracking()
                            .ToListAsync();

                        Debug.WriteLine($"Filtered query returned {transactions.Count} transactions");

                        return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
                    });
                }

                var fallbackTransactions = await ExecuteWithRetry(async () =>
                {
                    var query = _repository.Query().AsQueryable();

                    if (startDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                    if (endDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                    if (!string.IsNullOrEmpty(cashierId))
                        query = query.Where(t => t.CashierId == cashierId);

                    if (transactionType.HasValue)
                        query = query.Where(t => t.TransactionType == transactionType.Value);

                    if (status.HasValue)
                        query = query.Where(t => t.Status == status.Value);

                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        var lowercaseSearch = searchTerm.ToLower();
                        query = query.Where(t =>
                            t.CustomerName.ToLower().Contains(lowercaseSearch) ||
                            t.CashierName.ToLower().Contains(lowercaseSearch) ||
                            t.PaymentMethod.ToLower().Contains(lowercaseSearch) ||
                            t.TransactionId.ToString().Contains(searchTerm));
                    }

                    return await query
                        .Include(t => t.Customer)
                        .Include(t => t.TransactionDetails)
                            .ThenInclude(td => td.Product)
                        .OrderByDescending(t => t.TransactionDate)
                        .AsNoTracking()
                        .ToListAsync();
                });

                return _mapper.Map<IEnumerable<TransactionDTO>>(fallbackTransactions);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<decimal> GetTotalSalesAmountAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var query = _repository.Query()
                            .Where(t => t.TransactionType == TransactionType.Sale && t.Status == TransactionStatus.Completed);

                        if (startDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                        if (endDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                        return await query.SumAsync(t => t.TotalAmount);
                    });
                }

                return await ExecuteWithRetry(async () =>
                {
                    var query = _repository.Query()
                        .Where(t => t.TransactionType == TransactionType.Sale && t.Status == TransactionStatus.Completed);

                    if (startDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                    if (endDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                    return await query.SumAsync(t => t.TotalAmount);
                });
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<decimal> GetEmployeeSalesAmountAsync(string cashierId, DateTime? startDate = null, DateTime? endDate = null)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var query = _repository.Query()
                            .Where(t => t.CashierId == cashierId &&
                                       t.TransactionType == TransactionType.Sale &&
                                       t.Status == TransactionStatus.Completed);

                        if (startDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                        if (endDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                        return await query.SumAsync(t => t.TotalAmount);
                    });
                }

                return await ExecuteWithRetry(async () =>
                {
                    var query = _repository.Query()
                        .Where(t => t.CashierId == cashierId &&
                                   t.TransactionType == TransactionType.Sale &&
                                   t.Status == TransactionStatus.Completed);

                    if (startDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                    if (endDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                    return await query.SumAsync(t => t.TotalAmount);
                });
            }
            finally
            {
                _queryLock.Release();
            }
        }

        public async Task<Dictionary<string, decimal>> GetEmployeePerformanceAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            await _queryLock.WaitAsync();
            try
            {
                if (_dbContextScopeService != null)
                {
                    return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                    {
                        var query = _repository.Query()
                            .Where(t => t.TransactionType == TransactionType.Sale && t.Status == TransactionStatus.Completed);

                        if (startDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                        if (endDate.HasValue)
                            query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                        var performance = await query
                            .GroupBy(t => new { t.CashierId, t.CashierName })
                            .Select(g => new {
                                Employee = g.Key.CashierName ?? g.Key.CashierId,
                                TotalSales = g.Sum(t => t.TotalAmount)
                            })
                            .ToListAsync();

                        return performance.ToDictionary(p => p.Employee, p => p.TotalSales);
                    });
                }

                var fallbackPerformance = await ExecuteWithRetry(async () =>
                {
                    var query = _repository.Query()
                        .Where(t => t.TransactionType == TransactionType.Sale && t.Status == TransactionStatus.Completed);

                    if (startDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date >= startDate.Value.Date);

                    if (endDate.HasValue)
                        query = query.Where(t => t.TransactionDate.Date <= endDate.Value.Date);

                    return await query
                        .GroupBy(t => new { t.CashierId, t.CashierName })
                        .Select(g => new {
                            Employee = g.Key.CashierName ?? g.Key.CashierId,
                            TotalSales = g.Sum(t => t.TotalAmount)
                        })
                        .ToListAsync();
                });

                return fallbackPerformance.ToDictionary(p => p.Employee, p => p.TotalSales);
            }
            finally
            {
                _queryLock.Release();
            }
        }

        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
                {
                    Debug.WriteLine($"Attempt {attempt} failed: {ex.Message}. Retrying...");
                    await Task.Delay(100 * attempt);
                }
            }

            return await operation();
        }

        private static bool IsRetryableException(Exception ex)
        {
            return ex is ObjectDisposedException ||
                   ex is InvalidOperationException ||
                   (ex.Message.Contains("disposed") ||
                    ex.Message.Contains("second operation") ||
                    ex.Message.Contains("connection"));
        }

        public async Task<bool> UpdateTransactionDiscountAsync(int transactionId, decimal newDiscount)
        {
            var transactionLock = await GetTransactionLock(transactionId);
            await transactionLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var existingTransaction = await _repository.Query()
                            .Include(t => t.TransactionDetails)
                            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                        if (existingTransaction == null)
                            return false;

                        var subtotal = existingTransaction.TransactionDetails.Sum(td => td.Total);
                        existingTransaction.TotalAmount = Math.Max(0, subtotal - newDiscount);

                        var updateParameters = new[]
                        {
                            new SqlParameter("@transactionId", transactionId),
                            new SqlParameter("@totalAmount", existingTransaction.TotalAmount)
                        };

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                            "UPDATE Transactions SET TotalAmount = @totalAmount WHERE TransactionId = @transactionId",
                            updateParameters);

                        await transaction.CommitAsync();

                        var updatedDto = _mapper.Map<TransactionDTO>(existingTransaction);
                        _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update", updatedDto));

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error updating transaction discount: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                transactionLock.Release();
            }
        }

        public async Task<bool> UpdateTransactionDetailDiscountAsync(int transactionDetailId, decimal newDiscount)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var transactionDetail = await _unitOfWork.Context.Set<TransactionDetail>()
                        .Include(td => td.Transaction)
                        .FirstOrDefaultAsync(td => td.TransactionDetailId == transactionDetailId);

                    if (transactionDetail == null)
                        return false;

                    var transactionLock = await GetTransactionLock(transactionDetail.TransactionId);
                    await transactionLock.WaitAsync();
                    try
                    {
                        transactionDetail.Discount = Math.Max(0, newDiscount);
                        transactionDetail.Total = Math.Max(0, (transactionDetail.Quantity * transactionDetail.UnitPrice) - transactionDetail.Discount);

                        var updateParameters = new[]
                        {
                            new SqlParameter("@detailId", transactionDetailId),
                            new SqlParameter("@discount", transactionDetail.Discount),
                            new SqlParameter("@total", transactionDetail.Total)
                        };

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                            "UPDATE TransactionDetails SET Discount = @discount, Total = @total WHERE TransactionDetailId = @detailId",
                            updateParameters);

                        var newTransactionTotal = await _unitOfWork.Context.Set<TransactionDetail>()
                            .Where(td => td.TransactionId == transactionDetail.TransactionId)
                            .SumAsync(td => td.Total);

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                            "UPDATE Transactions SET TotalAmount = @totalAmount WHERE TransactionId = @transactionId",
                            new SqlParameter("@totalAmount", newTransactionTotal),
                            new SqlParameter("@transactionId", transactionDetail.TransactionId));

                        await transaction.CommitAsync();

                        var updatedTransaction = await GetTransactionWithDetailsAsync(transactionDetail.TransactionId);
                        if (updatedTransaction != null)
                        {
                            _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update", updatedTransaction));
                        }

                        return true;
                    }
                    finally
                    {
                        transactionLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error updating transaction detail discount: {ex}");
                    throw;
                }
            });
        }

        public async Task<bool> RemoveTransactionDetailAsync(int transactionDetailId)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var transactionDetail = await _unitOfWork.Context.Set<TransactionDetail>()
                        .Include(td => td.Transaction)
                        .Include(td => td.Product)
                        .FirstOrDefaultAsync(td => td.TransactionDetailId == transactionDetailId);

                    if (transactionDetail == null)
                        return false;

                    var transactionLock = await GetTransactionLock(transactionDetail.TransactionId);
                    await transactionLock.WaitAsync();
                    try
                    {
                        var productStockLock = await GetProductStockLock(transactionDetail.ProductId);
                        await productStockLock.WaitAsync();
                        try
                        {
                            if (transactionDetail.Transaction.TransactionType == TransactionType.Sale)
                            {
                                await RestockProductAsync(transactionDetail.ProductId, transactionDetail.Quantity,
                                    $"Restocked from removed transaction detail {transactionDetailId}");
                            }

                            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                                "DELETE FROM TransactionDetails WHERE TransactionDetailId = @detailId",
                                new SqlParameter("@detailId", transactionDetailId));

                            var newTotalAmount = await _unitOfWork.Context.Set<TransactionDetail>()
                                .Where(td => td.TransactionId == transactionDetail.TransactionId)
                                .SumAsync(td => td.Total);

                            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                                "UPDATE Transactions SET TotalAmount = @totalAmount WHERE TransactionId = @transactionId",
                                new SqlParameter("@totalAmount", newTotalAmount),
                                new SqlParameter("@transactionId", transactionDetail.TransactionId));

                            await transaction.CommitAsync();

                            var updatedTransaction = await GetTransactionWithDetailsAsync(transactionDetail.TransactionId);
                            if (updatedTransaction != null)
                            {
                                _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update", updatedTransaction));
                            }

                            return true;
                        }
                        finally
                        {
                            productStockLock.Release();
                        }
                    }
                    finally
                    {
                        transactionLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error removing transaction detail: {ex}");
                    throw;
                }
            });
        }

        public async Task<bool> UpdateTransactionDetailQuantityAsync(int transactionDetailId, decimal newQuantity)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var transactionDetail = await _unitOfWork.Context.Set<TransactionDetail>()
                        .Include(td => td.Transaction)
                        .FirstOrDefaultAsync(td => td.TransactionDetailId == transactionDetailId);

                    if (transactionDetail == null || newQuantity <= 0)
                        return false;

                    var transactionLock = await GetTransactionLock(transactionDetail.TransactionId);
                    await transactionLock.WaitAsync();
                    try
                    {
                        var productStockLock = await GetProductStockLock(transactionDetail.ProductId);
                        await productStockLock.WaitAsync();
                        try
                        {
                            var quantityDifference = transactionDetail.Quantity - newQuantity;

                            if (quantityDifference > 0 && transactionDetail.Transaction.TransactionType == TransactionType.Sale)
                            {
                                await RestockProductAsync(transactionDetail.ProductId, quantityDifference,
                                    $"Restocked from quantity reduction in transaction detail {transactionDetailId}");
                            }
                            else if (quantityDifference < 0 && transactionDetail.Transaction.TransactionType == TransactionType.Sale)
                            {
                                await DeductProductStockAsync(transactionDetail.ProductId, Math.Abs(quantityDifference),
                                    $"Deducted from quantity increase in transaction detail {transactionDetailId}");
                            }

                            transactionDetail.Quantity = newQuantity;
                            transactionDetail.Total = (newQuantity * transactionDetail.UnitPrice) - transactionDetail.Discount;

                            var updateParameters = new[]
                            {
                                new SqlParameter("@detailId", transactionDetailId),
                                new SqlParameter("@quantity", newQuantity),
                                new SqlParameter("@total", transactionDetail.Total)
                            };

                            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                                "UPDATE TransactionDetails SET Quantity = @quantity, Total = @total WHERE TransactionDetailId = @detailId",
                                updateParameters);

                            var newTransactionTotal = await _unitOfWork.Context.Set<TransactionDetail>()
                                .Where(td => td.TransactionId == transactionDetail.TransactionId)
                                .SumAsync(td => td.Total);

                            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                                "UPDATE Transactions SET TotalAmount = @totalAmount WHERE TransactionId = @transactionId",
                                new SqlParameter("@totalAmount", newTransactionTotal),
                                new SqlParameter("@transactionId", transactionDetail.TransactionId));

                            await transaction.CommitAsync();

                            var updatedTransaction = await GetTransactionWithDetailsAsync(transactionDetail.TransactionId);
                            if (updatedTransaction != null)
                            {
                                _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Update", updatedTransaction));
                            }

                            return true;
                        }
                        finally
                        {
                            productStockLock.Release();
                        }
                    }
                    finally
                    {
                        transactionLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Debug.WriteLine($"Error updating transaction detail quantity: {ex}");
                    throw;
                }
            });
        }

        public async Task<bool> DeleteTransactionWithRestockAsync(int transactionId)
        {
            var transactionLock = await GetTransactionLock(transactionId);
            await transactionLock.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var transactionToDelete = await _repository.Query()
                            .Include(t => t.TransactionDetails)
                            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                        if (transactionToDelete == null)
                            return false;

                        if (transactionToDelete.TransactionType == TransactionType.Sale)
                        {
                            var stockLockTasks = new List<Task<SemaphoreSlim>>();
                            var productIds = transactionToDelete.TransactionDetails.Select(td => td.ProductId).Distinct().ToList();

                            foreach (var productId in productIds)
                            {
                                stockLockTasks.Add(GetProductStockLock(productId));
                            }

                            var stockLocks = await Task.WhenAll(stockLockTasks);

                            try
                            {
                                foreach (var stockLock in stockLocks)
                                {
                                    await stockLock.WaitAsync();
                                }

                                foreach (var detail in transactionToDelete.TransactionDetails)
                                {
                                    await RestockProductAsync(detail.ProductId, detail.Quantity,
                                        $"Restocked from deleted transaction {transactionId}");
                                }
                            }
                            finally
                            {
                                foreach (var stockLock in stockLocks)
                                {
                                    stockLock.Release();
                                }
                            }
                        }

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM TransactionDetails WHERE TransactionId = @transactionId",
                            new SqlParameter("@transactionId", transactionId));

                        await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM Transactions WHERE TransactionId = @transactionId",
                            new SqlParameter("@transactionId", transactionId));

                        await transaction.CommitAsync();

                        var transactionDto = _mapper.Map<TransactionDTO>(transactionToDelete);
                        _eventAggregator.Publish(new EntityChangedEvent<TransactionDTO>("Delete", transactionDto));

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Debug.WriteLine($"Error deleting transaction: {ex}");
                        throw;
                    }
                });
            }
            finally
            {
                transactionLock.Release();
            }
        }

        private async Task RestockProductAsync(int productId, decimal quantity, string notes)
        {
            var restockParameters = new[]
            {
                new SqlParameter("@productId", productId),
                new SqlParameter("@quantity", quantity),
                new SqlParameter("@updatedAt", DateTime.Now)
            };

            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                "UPDATE Products SET CurrentStock = CurrentStock + @quantity, UpdatedAt = @updatedAt WHERE ProductId = @productId",
                restockParameters);

            var currentStock = await _unitOfWork.Context.Set<Product>()
                .Where(p => p.ProductId == productId)
                .Select(p => p.CurrentStock)
                .FirstOrDefaultAsync();

            var inventoryHistory = new InventoryHistory
            {
                ProductId = productId,
                QuantityChange = quantity,
                NewQuantity = currentStock,
                Type = "Restock",
                Notes = notes,
                Timestamp = DateTime.Now
            };

            await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(inventoryHistory);
        }

        private async Task DeductProductStockAsync(int productId, decimal quantity, string notes)
        {
            var deductParameters = new[]
            {
                new SqlParameter("@productId", productId),
                new SqlParameter("@quantity", quantity),
                new SqlParameter("@updatedAt", DateTime.Now)
            };

            await _unitOfWork.Context.Database.ExecuteSqlRawAsync(
                "UPDATE Products SET CurrentStock = CurrentStock - @quantity, UpdatedAt = @updatedAt WHERE ProductId = @productId",
                deductParameters);

            var currentStock = await _unitOfWork.Context.Set<Product>()
                .Where(p => p.ProductId == productId)
                .Select(p => p.CurrentStock)
                .FirstOrDefaultAsync();

            var inventoryHistory = new InventoryHistory
            {
                ProductId = productId,
                QuantityChange = -quantity,
                NewQuantity = currentStock,
                Type = "Sale Adjustment",
                Notes = notes,
                Timestamp = DateTime.Now
            };

            await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(inventoryHistory);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var lockPair in _transactionLocks)
                {
                    lockPair.Value?.Dispose();
                }
                _transactionLocks.Clear();

                foreach (var lockPair in _productStockLocks)
                {
                    lockPair.Value?.Dispose();
                }
                _productStockLocks.Clear();

                _lockManagerSemaphore?.Dispose();
                _queryLock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}