// QuickTechSystems.Application/Services/TransactionCalculator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services
{
    public class TransactionCalculator
    {
        // Standardized transaction types
        public static class TransactionTypes
        {
            public const string Open = "Open";
            public const string Close = "Close";
            public const string CashSale = "Cash Sale";
            public const string CashIn = "Cash In";
            public const string CashOut = "Cash Out";
            public const string Expense = "Expense";
            public const string SupplierPayment = "Supplier Payment";
            public const string Return = "Return";
            public const string CashReceipt = "Cash Receipt";
            public const string SalaryWithdrawal = "Salary Withdrawal";
            public const string Modification = "Transaction Modification";
        }

        // Standardized action types
        public static class ActionTypes
        {
            public const string Increase = "Increase";
            public const string Decrease = "Decrease";
            public const string Modification = "Transaction Modification";
            public const string Opening = "Open";
            public const string Closing = "Close";
        }

        /// <summary>
        /// Calculates new balance after a transaction
        /// This is the single source of truth for all balance calculations
        /// </summary>
        public static decimal CalculateNewBalance(decimal currentBalance, string transactionType, decimal amount, string actionType = null)
        {
            // Handle special cases first
            if (actionType == ActionTypes.Modification)
            {
                // For modifications, use the signed amount directly
                return currentBalance + amount;
            }

            if (transactionType == TransactionTypes.Open)
            {
                // Opening balance replaces current balance
                return Math.Abs(amount);
            }

            // Use absolute amount for all other calculations
            var absAmount = Math.Abs(amount);

            return transactionType switch
            {
                TransactionTypes.CashSale or
                TransactionTypes.CashIn or
                TransactionTypes.CashReceipt => currentBalance + absAmount,

                TransactionTypes.Expense or
                TransactionTypes.SupplierPayment or
                TransactionTypes.Return or
                TransactionTypes.CashOut or
                TransactionTypes.SalaryWithdrawal => currentBalance - absAmount,

                TransactionTypes.Close => Math.Abs(amount), // Final balance for closing

                _ => currentBalance // No change for unknown types
            };
        }

        /// <summary>
        /// Calculates running balance from a list of transactions
        /// </summary>
        public static decimal CalculateRunningBalance(decimal openingBalance, IEnumerable<DrawerTransactionDTO> transactions)
        {
            var balance = openingBalance;

            foreach (var transaction in transactions.OrderBy(t => t.Timestamp))
            {
                balance = CalculateNewBalance(balance, transaction.Type, transaction.Amount, transaction.ActionType);
            }

            return balance;
        }

        /// <summary>
        /// Determines if a transaction type decreases the balance
        /// </summary>
        public static bool IsDebitTransaction(string transactionType)
        {
            return transactionType switch
            {
                TransactionTypes.Expense or
                TransactionTypes.SupplierPayment or
                TransactionTypes.Return or
                TransactionTypes.CashOut or
                TransactionTypes.SalaryWithdrawal => true,
                _ => false
            };
        }

        /// <summary>
        /// Determines if a transaction type increases the balance
        /// </summary>
        public static bool IsCreditTransaction(string transactionType)
        {
            return transactionType switch
            {
                TransactionTypes.CashSale or
                TransactionTypes.CashIn or
                TransactionTypes.CashReceipt => true,
                _ => false
            };
        }

        /// <summary>
        /// Validates if a transaction is possible given current balance
        /// </summary>
        public static bool ValidateTransaction(decimal currentBalance, string transactionType, decimal amount)
        {
            if (amount < 0) return false;

            // Check if debit transaction has sufficient funds
            if (IsDebitTransaction(transactionType))
            {
                return currentBalance >= Math.Abs(amount);
            }

            return true; // Credit transactions are always valid
        }

        /// <summary>
        /// Calculates financial totals from transactions
        /// </summary>
        public static FinancialTotals CalculateFinancialTotals(IEnumerable<DrawerTransactionDTO> transactions, DateTime? filterDate = null)
        {
            var filteredTransactions = transactions.AsEnumerable();

            if (filterDate.HasValue)
            {
                filteredTransactions = transactions.Where(t => t.Timestamp.Date == filterDate.Value.Date);
            }

            var totals = new FinancialTotals();

            foreach (var transaction in filteredTransactions)
            {
                var absAmount = Math.Abs(transaction.Amount);

                switch (transaction.Type)
                {
                    case TransactionTypes.CashSale:
                        if (transaction.ActionType == ActionTypes.Modification)
                        {
                            totals.TotalSales += transaction.Amount; // Use signed amount for modifications
                        }
                        else
                        {
                            totals.TotalSales += absAmount;
                        }
                        break;

                    case TransactionTypes.CashReceipt:
                        totals.TotalSales += absAmount; // Treat cash receipts as sales
                        break;

                    case TransactionTypes.Return:
                        totals.TotalReturns += absAmount;
                        break;

                    case TransactionTypes.Expense:
                        if (transaction.ActionType == ActionTypes.Modification)
                        {
                            totals.TotalExpenses += transaction.Amount; // Use signed amount for modifications
                        }
                        else
                        {
                            totals.TotalExpenses += absAmount;
                        }
                        break;

                    case TransactionTypes.SupplierPayment:
                        if (transaction.ActionType == ActionTypes.Modification)
                        {
                            totals.TotalSupplierPayments += transaction.Amount;
                        }
                        else
                        {
                            totals.TotalSupplierPayments += absAmount;
                        }
                        break;

                    case TransactionTypes.CashIn:
                        totals.TotalCashIn += absAmount;
                        break;

                    case TransactionTypes.CashOut:
                        totals.TotalCashOut += absAmount;
                        break;
                }
            }

            // Calculate derived totals
            totals.NetSales = totals.TotalSales - totals.TotalReturns;
            totals.NetCashFlow = totals.TotalSales - (totals.TotalExpenses + totals.TotalSupplierPayments + totals.TotalReturns);

            return totals;
        }
    }

    /// <summary>
    /// Represents calculated financial totals
    /// </summary>
    public class FinancialTotals
    {
        public decimal TotalSales { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal TotalSupplierPayments { get; set; }
        public decimal TotalCashIn { get; set; }
        public decimal TotalCashOut { get; set; }
        public decimal NetSales { get; set; }
        public decimal NetCashFlow { get; set; }
    }
}