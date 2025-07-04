using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces;
using QuickTechSystems.Application.Mappings;

namespace QuickTechSystems.Application.Services
{
    public partial class DrawerService : BaseService<Drawer, DrawerDTO>, IDrawerService
    {
        private new readonly IUnitOfWork _unitOfWork;
        private new readonly IMapper _mapper;
        private new readonly IEventAggregator _eventAggregator;
        private new readonly IDbContextScopeService _dbContextScopeService;
        private readonly IGenericRepository<DrawerTransaction> _drawerTransactionRepository; // Add this line

        private static readonly Dictionary<string, (bool IsIncoming, bool UpdatesSales, bool UpdatesExpenses)> TransactionTypeConfig = new()
        {
            ["Open"] = (true, false, false),
            ["Cash Sale"] = (true, true, false),
            ["Cash In"] = (true, false, false),
            ["Cash Receipt"] = (true, true, false),
            ["Expense"] = (false, false, true),
            ["Internet Expenses"] = (false, false, true),
            ["Supplier Payment"] = (false, false, true),
            ["Cash Out"] = (false, false, false),
            ["Salary Withdrawal"] = (false, false, true),
            ["Return"] = (false, false, false),
            ["Quote Payment"] = (true, true, false)
        };

        public DrawerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(unitOfWork, mapper, eventAggregator, dbContextScopeService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _eventAggregator = eventAggregator;
            _dbContextScopeService = dbContextScopeService;
            _drawerTransactionRepository = _unitOfWork.GetRepository<DrawerTransaction>();
        }
        public async Task<DrawerDTO?> GetCurrentDrawerAsync()
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var drawerEntity = await _unitOfWork.Drawers.Query()
                        .Where(d => d.Status == "Open")
                        .OrderByDescending(d => d.OpenedAt)
                        .FirstOrDefaultAsync();

                    return drawerEntity != null ? _mapper.Map<DrawerDTO>(drawerEntity) : null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting current drawer: {ex.Message}");
                    return null;
                }
            }, "GetCurrentDrawer");
        }

        public async Task<DrawerDTO> ProcessTransactionAsync(decimal amount, string transactionType, string description, string reference = "")
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .OrderByDescending(d => d.OpenedAt)
                    .FirstOrDefaultAsync();

                if (drawer == null)
                    throw new InvalidOperationException("No open drawer found. Please open a drawer first.");

                ValidateTransactionInternal(amount, transactionType, drawer);

                var config = GetTransactionConfig(transactionType);
                var adjustedAmount = GetAdjustedAmount(transactionType, amount);
                var newBalance = CalculateNewBalance(transactionType, drawer.CurrentBalance, adjustedAmount);

                var transaction = CreateDrawerTransaction(drawer.DrawerId, transactionType, adjustedAmount, newBalance,
                    EnhanceDescription(description, reference), reference);

                await UpdateDrawerTotalsAsync(drawer, transactionType, adjustedAmount, config);
                drawer.CurrentBalance = newBalance;

                await _drawerTransactionRepository.AddAsync(transaction);
                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent(transactionType, adjustedAmount, EnhanceDescription(description, reference));

                return _mapper.Map<DrawerDTO>(drawer);
            }, "ProcessTransaction");
        }

        public Task<DrawerDTO> ProcessCashSaleAsync(decimal amount, string reference) =>
            ProcessTransactionAsync(amount, "Cash Sale", "Cash sale transaction", reference);

        public Task<DrawerDTO> ProcessExpenseAsync(decimal amount, string expenseType, string description) =>
            ProcessTransactionAsync(amount, "Expense", description, expenseType);

        public Task<DrawerDTO> ProcessSupplierPaymentAsync(decimal amount, string supplierName, string reference) =>
            ProcessTransactionAsync(amount, "Supplier Payment", $"Payment to supplier: {supplierName}", reference);

        public Task<DrawerDTO> ProcessQuotePaymentAsync(decimal amount, string customerName, string quoteNumber) =>
            ProcessTransactionAsync(amount, "Quote Payment", $"Quote payment from {customerName}", quoteNumber);

        public async Task<bool> ProcessCashReceiptAsync(decimal amount, string description)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .OrderByDescending(d => d.OpenedAt)
                    .FirstOrDefaultAsync();

                if (drawer == null)
                    throw new InvalidOperationException("No open drawer found. Please open a drawer first.");

                var transaction = CreateDrawerTransaction(drawer.DrawerId, "Cash Receipt", amount,
                    drawer.CurrentBalance + amount, description);

                UpdateDrawerFinancials(drawer, amount, true, true, false);
                transaction.Balance = drawer.CurrentBalance;

                await _drawerTransactionRepository.AddAsync(transaction);
                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent("Cash Receipt", amount, description);
                return true;
            }, "ProcessCashReceipt");
        }

        public async Task<bool> ProcessSupplierInvoiceAsync(decimal amount, string supplierName, string reference)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .OrderByDescending(d => d.OpenedAt)
                    .FirstOrDefaultAsync();

                if (drawer == null || drawer.CurrentBalance < amount)
                    throw new InvalidOperationException(drawer == null ? "No active drawer found" : "Insufficient funds in drawer");

                var transaction = CreateDrawerTransaction(drawer.DrawerId, "Expense", amount,
                    drawer.CurrentBalance - amount, $"Supplier Invoice Payment: {supplierName}", reference);

                UpdateDrawerFinancials(drawer, amount, false, false, true);

                await _drawerTransactionRepository.AddAsync(transaction);
                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();
                return true;
            }, "ProcessSupplierInvoice");
        }

        public async Task<bool> UpdateDrawerTransactionForModifiedSaleAsync(int transactionId, decimal oldAmount, decimal newAmount, string description)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var amountDifference = newAmount - oldAmount;
                if (Math.Abs(amountDifference) < 0.01m) return true;

                var drawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .OrderByDescending(d => d.OpenedAt)
                    .FirstOrDefaultAsync();

                if (drawer == null)
                    throw new InvalidOperationException("No open drawer found");

                var drawerTransactions = await _drawerTransactionRepository.Query()
                    .Where(dt => dt.TransactionReference == transactionId.ToString() ||
                                dt.TransactionReference == $"Transaction #{transactionId}")
                    .ToListAsync();

                if (!drawerTransactions.Any()) return false;

                UpdateDrawerForModification(drawer, drawerTransactions.First().Type, amountDifference);

                var modificationEntry = CreateDrawerTransaction(drawer.DrawerId, drawerTransactions.First().Type,
                    amountDifference, drawer.CurrentBalance,
                    FormatModificationDescription(description, transactionId), $"Transaction #{transactionId} (Modified)");
                modificationEntry.ActionType = "Transaction Modification";

                await _drawerTransactionRepository.AddAsync(modificationEntry);
                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent("Transaction Modification", amountDifference, modificationEntry.Description);
                return true;
            }, "UpdateDrawerTransactionForModifiedSale");
        }

        public async Task<DrawerDTO> OpenDrawerAsync(decimal openingBalance, string cashierId, string cashierName)
        {
            if (string.IsNullOrEmpty(cashierId) || string.IsNullOrEmpty(cashierName))
                throw new InvalidOperationException("Cashier information is required");

            return await ExecuteServiceOperationAsync(async () =>
            {
                var currentDrawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .FirstOrDefaultAsync();

                if (currentDrawer != null)
                    throw new InvalidOperationException("There is already an open drawer");

                var drawer = new Drawer
                {
                    OpeningBalance = openingBalance,
                    CurrentBalance = openingBalance,
                    OpenedAt = DateTime.Now,
                    Status = "Open",
                    CashierId = cashierId,
                    CashierName = cashierName,
                    CashIn = 0,
                    CashOut = 0
                };

                await _unitOfWork.Drawers.AddAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                var openingTransaction = CreateDrawerTransaction(drawer.DrawerId, "Open", openingBalance,
                    openingBalance, $"Drawer opened by {cashierName}");
                openingTransaction.ActionType = "Open";

                await _drawerTransactionRepository.AddAsync(openingTransaction);
                await _unitOfWork.SaveChangesAsync();

                return _mapper.Map<DrawerDTO>(drawer);
            }, "OpenDrawer");
        }

        public async Task<DrawerDTO> CloseDrawerAsync(decimal finalBalance, string? notes)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Include(d => d.Transactions)
                    .FirstOrDefaultAsync(d => d.Status == "Open");

                if (drawer == null)
                    throw new InvalidOperationException("No open drawer found");

                if (!drawer.Transactions.Any(t => t.Type == "Close"))
                {
                    var closingTransaction = CreateDrawerTransaction(drawer.DrawerId, "Close", finalBalance,
                        finalBalance, $"Drawer closed by {drawer.CashierName} with final balance of {finalBalance:C2}");
                    closingTransaction.ActionType = "Close";
                    drawer.Transactions.Add(closingTransaction);
                }

                drawer.CurrentBalance = finalBalance;
                drawer.ClosedAt = DateTime.Now;
                drawer.Status = "Closed";
                drawer.Notes = notes;

                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                var difference = finalBalance - (drawer.OpeningBalance + drawer.CashIn - drawer.CashOut);
                PublishTransactionEvent("Close", difference,
                    $"Drawer closed by {drawer.CashierName} with {(difference >= 0 ? "surplus" : "shortage")} of {Math.Abs(difference):C2}");

                return _mapper.Map<DrawerDTO>(drawer);
            }, "CloseDrawer");
        }

        public async Task<DrawerDTO> AddCashTransactionAsync(decimal amount, bool isIn, string description = null)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Where(d => d.Status == "Open")
                    .FirstOrDefaultAsync();

                if (drawer == null)
                    throw new InvalidOperationException("No open drawer found");

                if (!isIn && amount > drawer.CurrentBalance)
                    throw new InvalidOperationException("Cannot remove more cash than current balance");

                var actionType = isIn ? "Cash In" : "Cash Out";
                var finalDescription = description ?? (isIn ? "Cash added to drawer" : "Cash removed from drawer");

                UpdateDrawerFinancials(drawer, amount, isIn, false, false);
                await _unitOfWork.Drawers.UpdateAsync(drawer);

                var transaction = CreateDrawerTransaction(drawer.DrawerId, actionType, isIn ? amount : -amount, drawer.CurrentBalance, finalDescription);
                await _drawerTransactionRepository.AddAsync(transaction);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent(actionType, isIn ? amount : -amount, finalDescription);
                return _mapper.Map<DrawerDTO>(drawer);
            }, "AddCashTransaction");
        }

        public Task<DrawerDTO> AddCashTransactionAsync(decimal amount, bool isIn) =>
            AddCashTransactionAsync(amount, isIn, null);

        public async Task<IEnumerable<DrawerTransactionDTO>> GetDrawerHistoryAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var transactions = await _drawerTransactionRepository.Query()
                    .Where(dt => dt.DrawerId == drawerId)
                    .OrderBy(dt => dt.Timestamp)
                    .ToListAsync();

                var result = _mapper.Map<IEnumerable<DrawerTransactionDTO>>(transactions);
                CalculateRunningBalances(result);
                return result;
            }, "GetDrawerHistory");
        }

        public async Task<(decimal Sales, decimal SupplierPayments, decimal Expenses)> GetFinancialSummaryAsync(DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var transactions = await _drawerTransactionRepository.Query()
                        .Where(t => t.Timestamp.Date >= startDate.Date &&
                                   t.Timestamp.Date <= endDate.Date &&
                                   t.Drawer.Status == "Open")
                        .AsNoTracking()
                        .ToListAsync();

                    var summary = transactions.GroupBy(t => GetSummaryCategory(t.Type))
                        .ToDictionary(g => g.Key, g => g.Sum(t => Math.Abs(t.Amount)));

                    return (
                        Sales: summary.GetValueOrDefault("Sales", 0),
                        SupplierPayments: summary.GetValueOrDefault("SupplierPayments", 0),
                        Expenses: summary.GetValueOrDefault("Expenses", 0)
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in GetFinancialSummaryAsync: {ex.Message}");
                    throw;
                }
            }, "GetFinancialSummary");
        }

        public async Task<IEnumerable<DrawerDTO>> GetAllDrawerSessionsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var query = _unitOfWork.Drawers.Query();

                    if (startDate.HasValue)
                        query = query.Where(d => d.OpenedAt.Date >= startDate.Value.Date);
                    if (endDate.HasValue)
                        query = query.Where(d => d.OpenedAt.Date <= endDate.Value.Date);

                    var sessions = await query.OrderByDescending(d => d.OpenedAt).ToListAsync();
                    return _mapper.Map<IEnumerable<DrawerDTO>>(sessions);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting drawer sessions: {ex.Message}");
                    throw;
                }
            }, "GetAllDrawerSessions");
        }

        public async Task<DrawerDTO?> GetDrawerSessionByIdAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                try
                {
                    var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                    return drawer != null ? _mapper.Map<DrawerDTO>(drawer) : null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting drawer session by ID: {ex.Message}");
                    return null;
                }
            }, "GetDrawerSessionById");
        }

        public async Task LogDrawerActionAsync(int drawerId, string actionType, string description)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                if (drawer == null)
                    throw new InvalidOperationException("Drawer not found");

                var transaction = CreateDrawerTransaction(drawerId, actionType, 0, drawer.CurrentBalance, description);
                await _drawerTransactionRepository.AddAsync(transaction);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent(actionType, 0, description);
            }, "LogDrawerAction");
        }

        public Task<decimal> GetCurrentBalanceAsync() =>
            GetCurrentDrawerAsync().ContinueWith(t => t.Result?.CurrentBalance ?? 0);

        public async Task<DrawerDTO> AdjustBalanceAsync(int drawerId, decimal newBalance, string reason)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                if (drawer == null)
                    throw new InvalidOperationException("Drawer not found");

                var adjustment = newBalance - drawer.CurrentBalance;
                drawer.CurrentBalance = newBalance;

                var transaction = CreateDrawerTransaction(drawerId, "Balance Adjustment", adjustment, newBalance, reason);
                await _drawerTransactionRepository.AddAsync(transaction);

                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                return _mapper.Map<DrawerDTO>(drawer);
            }, "AdjustBalance");
        }

        public async Task RecalculateDrawerTotalsAsync(int drawerId)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Include(d => d.Transactions)
                    .FirstOrDefaultAsync(d => d.DrawerId == drawerId);

                if (drawer == null) return;

                ResetDrawerTotals(drawer);
                decimal runningBalance = drawer.OpeningBalance;

                foreach (var transaction in drawer.Transactions.OrderBy(t => t.Timestamp))
                {
                    var config = GetTransactionConfig(transaction.Type);
                    var absAmount = Math.Abs(transaction.Amount);

                    UpdateDrawerTotalsFromTransaction(drawer, transaction.Type, absAmount, config);
                    runningBalance = CalculateNewBalance(transaction.Type, runningBalance, transaction.Amount);
                }

                drawer.CurrentBalance = runningBalance;
                CalculateNetAmounts(drawer);

                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();

                PublishTransactionEvent("Recalculation", 0, "Drawer totals recalculated");
            }, "RecalculateDrawerTotals");
        }

        public async Task<IEnumerable<DrawerTransactionDTO>> GetTransactionsByTypeAsync(string transactionType, DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var transactions = await _unitOfWork.GetRepository<DrawerHistoryEntry>().Query()
                    .Where(h => h.ActionType == transactionType && h.Timestamp >= startDate && h.Timestamp <= endDate)
                    .OrderByDescending(h => h.Timestamp)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<DrawerTransactionDTO>>(transactions);
            }, "GetTransactionsByType");
        }

        public async Task<decimal> GetTotalByTransactionTypeAsync(string transactionType, DateTime startDate, DateTime endDate)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                return await _unitOfWork.GetRepository<DrawerHistoryEntry>().Query()
                    .Where(h => h.ActionType == transactionType && h.Timestamp >= startDate && h.Timestamp <= endDate)
                    .SumAsync(h => h.Amount);
            }, "GetTotalByTransactionType");
        }

        public Task<bool> ValidateTransactionAsync(decimal amount, bool isCashOut = false) =>
            GetCurrentDrawerAsync().ContinueWith(t =>
                amount > 0 && t.Result != null && (!isCashOut || t.Result.CurrentBalance >= amount));

        public async Task ResetDailyTotalsAsync(int drawerId)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                if (drawer == null)
                    throw new InvalidOperationException("Drawer not found");

                drawer.DailySales = drawer.DailyExpenses = drawer.DailySupplierPayments = 0;
                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();
            }, "ResetDailyTotals");
        }

        public async Task<(decimal Sales, decimal Expenses)> GetDailyTotalsAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var today = DateTime.Today;

                var transactions = await _drawerTransactionRepository.Query()
                    .Where(t => t.DrawerId == drawerId && t.Timestamp.Date == today)
                    .ToListAsync();

                return (
                    Sales: transactions.Where(t => t.Type == "Cash Sale").Sum(t => Math.Abs(t.Amount)),
                    Expenses: transactions.Where(t => t.Type == "Expense" || t.Type == "Supplier Payment").Sum(t => Math.Abs(t.Amount))
                );
            }, "GetDailyTotals");
        }

        public async Task UpdateDailyCalculationsAsync(int drawerId)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                var (sales, expenses) = await GetDailyTotalsAsync(drawerId);

                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                if (drawer == null)
                    throw new InvalidOperationException("Drawer not found");

                drawer.DailySales = sales;
                drawer.DailyExpenses = expenses;

                await _unitOfWork.Drawers.UpdateAsync(drawer);
                await _unitOfWork.SaveChangesAsync();
            }, "UpdateDailyCalculations");
        }

        public async Task<bool> VerifyDrawerBalanceAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.Query()
                    .Include(d => d.Transactions)
                    .FirstOrDefaultAsync(d => d.DrawerId == drawerId);

                return ValidateCalculatedBalance(drawer);
            }, "VerifyDrawerBalance");
        }

        public async Task<decimal> GetExpectedBalanceAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                return drawer?.OpeningBalance + drawer?.CashIn - drawer?.CashOut ?? 0;
            }, "GetExpectedBalance");
        }

        public async Task<decimal> GetActualBalanceAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                return drawer?.CurrentBalance ?? 0;
            }, "GetActualBalance");
        }

        public async Task<decimal> GetBalanceDifferenceAsync(int drawerId)
        {
            var expected = await GetExpectedBalanceAsync(drawerId);
            var actual = await GetActualBalanceAsync(drawerId);
            return actual - expected;
        }

        public async Task<IEnumerable<DrawerTransactionDTO>> GetDiscrepancyTransactionsAsync(int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var transactions = await _drawerTransactionRepository.Query()
                    .Where(t => t.DrawerId == drawerId)
                    .OrderByDescending(t => t.Timestamp)
                    .ToListAsync();

                var discrepancies = new List<DrawerTransaction>();
                decimal runningBalance = 0;

                foreach (var transaction in transactions.OrderBy(t => t.Timestamp))
                {
                    runningBalance = CalculateNewBalance(transaction.Type, runningBalance, transaction.Amount);
                    if (Math.Abs(runningBalance - transaction.Balance) > 0.01m)
                        discrepancies.Add(transaction);
                }

                return _mapper.Map<IEnumerable<DrawerTransactionDTO>>(discrepancies);
            }, "GetDiscrepancyTransactions");
        }

        public Task LogDrawerAuditAsync(int drawerId, string action, string description) =>
            LogDrawerActionAsync(drawerId, "Audit", $"{action}: {description}");

        public async Task<bool> ValidateDrawerAccessAsync(string cashierId, int drawerId)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var drawer = await _unitOfWork.Drawers.GetByIdAsync(drawerId);
                if (drawer?.CashierId == cashierId) return true;

                if (drawer != null)
                    await LogDrawerAuditAsync(drawerId, "Access Validation", $"Unauthorized access attempt by cashier {cashierId}");

                return false;
            }, "ValidateDrawerAccess");
        }

        private void ValidateTransactionInternal(decimal amount, string transactionType, Drawer drawer)
        {
            if (amount <= 0) throw new InvalidOperationException("Amount must be greater than zero");

            var config = GetTransactionConfig(transactionType);
            if (!config.IsIncoming && amount > drawer.CurrentBalance)
                throw new InvalidOperationException("Insufficient funds in drawer");
        }

        private (bool IsIncoming, bool UpdatesSales, bool UpdatesExpenses) GetTransactionConfig(string transactionType) =>
            TransactionTypeConfig.TryGetValue(transactionType, out var config) ? config : (false, false, false);

        private decimal GetAdjustedAmount(string transactionType, decimal amount)
        {
            var config = GetTransactionConfig(transactionType);
            return config.IsIncoming ? Math.Abs(amount) : -Math.Abs(amount);
        }

        private decimal CalculateNewBalance(string transactionType, decimal currentBalance, decimal amount) =>
            transactionType.ToLower() switch
            {
                "open" => Math.Abs(amount),
                _ when GetTransactionConfig(transactionType).IsIncoming => currentBalance + Math.Abs(amount),
                _ => currentBalance - Math.Abs(amount)
            };

        private DrawerTransaction CreateDrawerTransaction(int drawerId, string type, decimal amount, decimal balance, string description, string reference = "")
        {
            return new DrawerTransaction
            {
                DrawerId = drawerId,
                Timestamp = DateTime.Now,
                Type = type,
                Amount = amount,
                Balance = balance,
                Description = description,
                ActionType = type,
                TransactionReference = reference,
                PaymentMethod = "Cash"
            };
        }

        private async Task UpdateDrawerTotalsAsync(Drawer drawer, string transactionType, decimal amount, (bool IsIncoming, bool UpdatesSales, bool UpdatesExpenses) config)
        {
            var absAmount = Math.Abs(amount);

            if (config.UpdatesSales)
            {
                drawer.TotalSales += absAmount;
                drawer.CashIn += absAmount;
            }
            else if (config.UpdatesExpenses)
            {
                drawer.TotalExpenses += absAmount;
                drawer.CashOut += absAmount;
            }
            else if (transactionType.ToLower() == "cash out")
            {
                drawer.CashOut += absAmount;
            }
            else if (transactionType.ToLower() == "cash in")
            {
                drawer.CashIn += absAmount;
            }

            drawer.LastUpdated = DateTime.Now;
            await CalculateNetAmountsAsync(drawer);
        }

        private void UpdateDrawerFinancials(Drawer drawer, decimal amount, bool isIncoming, bool updatesSales, bool updatesExpenses)
        {
            var absAmount = Math.Abs(amount);

            if (isIncoming)
            {
                drawer.CurrentBalance += absAmount;
                drawer.CashIn += absAmount;
                if (updatesSales) drawer.TotalSales += absAmount;
            }
            else
            {
                drawer.CurrentBalance -= absAmount;
                drawer.CashOut += absAmount;
                if (updatesExpenses) drawer.TotalExpenses += absAmount;
            }

            drawer.LastUpdated = DateTime.Now;
        }

        private void UpdateDrawerForModification(Drawer drawer, string transactionType, decimal amountDifference)
        {
            drawer.CurrentBalance += amountDifference;

            switch (transactionType.ToLower())
            {
                case "cash sale":
                    drawer.TotalSales += amountDifference;
                    drawer.CashIn += amountDifference;
                    break;
                case "expense":
                case "supplier payment":
                    drawer.TotalExpenses += amountDifference;
                    drawer.CashOut += amountDifference;
                    break;
            }

            drawer.LastUpdated = DateTime.Now;
        }

        private async Task CalculateNetAmountsAsync(Drawer drawer)
        {
            drawer.NetSales = drawer.TotalSales;
            drawer.NetCashFlow = drawer.TotalSales - drawer.TotalExpenses;
        }

        private void CalculateNetAmounts(Drawer drawer)
        {
            drawer.NetSales = drawer.TotalSales;
            drawer.NetCashFlow = drawer.TotalSales - drawer.TotalExpenses;
        }

        private string EnhanceDescription(string description, string reference)
        {
            if (string.IsNullOrEmpty(reference) || description.Contains(reference)) return description;

            return reference.Contains("#")
                ? $"{description} {reference.Substring(reference.IndexOf("#"))}"
                : $"{description} ({reference})";
        }

        private string FormatModificationDescription(string description, int transactionId)
        {
            if (!string.IsNullOrEmpty(description))
                return description.Contains($"#{transactionId}") ? description : $"{description} (Transaction #{transactionId})";

            return $"Modified Transaction #{transactionId}";
        }

        private void PublishTransactionEvent(string type, decimal amount, string description) =>
            _eventAggregator.Publish(new DrawerUpdateEvent(type, amount, description));

        private void CalculateRunningBalances(IEnumerable<DrawerTransactionDTO> transactions)
        {
            decimal runningBalance = 0;
            foreach (var tx in transactions.OrderBy(t => t.Timestamp))
            {
                if (tx.Type.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    runningBalance = tx.Amount;
                }
                else if (GetTransactionConfig(tx.Type).IsIncoming ||
                        (tx.ActionType?.Equals("Increase", StringComparison.OrdinalIgnoreCase) == true))
                {
                    runningBalance += Math.Abs(tx.Amount);
                }
                else
                {
                    runningBalance -= Math.Abs(tx.Amount);
                }
                tx.ResultingBalance = runningBalance;
            }
        }

        private string GetSummaryCategory(string transactionType) =>
            transactionType.ToLower() switch
            {
                "cash sale" => "Sales",
                "supplier payment" => "SupplierPayments",
                "expense" or "internet expenses" => "Expenses",
                _ => "Other"
            };

        private void ResetDrawerTotals(Drawer drawer)
        {
            drawer.TotalSales = drawer.TotalExpenses = drawer.TotalSupplierPayments =
            drawer.CashIn = drawer.CashOut = 0;
        }

        private void UpdateDrawerTotalsFromTransaction(Drawer drawer, string transactionType, decimal absAmount, (bool IsIncoming, bool UpdatesSales, bool UpdatesExpenses) config)
        {
            if (config.UpdatesSales) drawer.TotalSales += absAmount;
            if (config.UpdatesExpenses) drawer.TotalExpenses += absAmount;
            if (transactionType.ToLower() == "supplier payment") drawer.TotalSupplierPayments += absAmount;
        }

        private bool ValidateCalculatedBalance(Drawer drawer)
        {
            if (drawer == null) return false;

            decimal calculatedBalance = drawer.OpeningBalance;
            foreach (var transaction in drawer.Transactions.OrderBy(t => t.Timestamp))
                calculatedBalance = CalculateNewBalance(transaction.Type, calculatedBalance, transaction.Amount);

            return Math.Abs(calculatedBalance - drawer.CurrentBalance) < 0.01m;
        }
    }
}