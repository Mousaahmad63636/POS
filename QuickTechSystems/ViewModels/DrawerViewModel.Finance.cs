using System;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class DrawerViewModel
    {
        private decimal _totalSales;
        private decimal _totalExpenses;
        private decimal _totalDebtPayments;
        private decimal _totalSupplierPayments;
        private decimal _netEarnings;
        private decimal _totalReturns;
        private decimal _netSales;
        private decimal _netCashflow;
        private decimal _dailySales;
        private decimal _dailyReturns;
        private decimal _dailyExpenses;
        private decimal _supplierPayments;
        private decimal _debtPayments;

        private async Task LoadFinancialOverviewAsync()
        {
            try
            {
                if (CurrentDrawer == null)
                {
                    Debug.WriteLine("LoadFinancialOverviewAsync: CurrentDrawer is null");
                    return;
                }

                Debug.WriteLine($"LoadFinancialOverviewAsync: Starting calculation for Drawer {CurrentDrawer.DrawerId}");

                var todayTransactions = DrawerHistory
                    .Where(t => t.Timestamp.Date == DateTime.Today)
                    .ToList();

                Debug.WriteLine($"Found {todayTransactions.Count} transactions for today");
                foreach (var t in todayTransactions)
                {
                    Debug.WriteLine($"Transaction: Type={t.Type}, Amount={t.Amount}, Balance={t.Balance}");
                }

                // Calculate all financial totals
                TotalSales = todayTransactions
                    .Where(t => t.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"TotalSales calculated: {TotalSales}");

                TotalReturns = todayTransactions
                    .Where(t => t.Type.Equals("Return", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"TotalReturns calculated: {TotalReturns}");

                // Calculate all types of expenses
                var regularExpenses = todayTransactions
                    .Where(t => t.Type.Equals("Expense", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"RegularExpenses calculated: {regularExpenses}");

                var salaryWithdrawals = todayTransactions
                    .Where(t => t.Type.Equals("Salary Withdrawal", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"SalaryWithdrawals calculated: {salaryWithdrawals}");

                var supplierPayments = todayTransactions
                    .Where(t => t.Type.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"SupplierPayments calculated: {supplierPayments}");

                // Sum all expenses
                TotalExpenses = regularExpenses + salaryWithdrawals + supplierPayments;
                Debug.WriteLine($"TotalExpenses calculated: {TotalExpenses}");

                TotalSupplierPayments = supplierPayments;
                Debug.WriteLine($"TotalSupplierPayments set: {TotalSupplierPayments}");

                TotalDebtPayments = todayTransactions
                    .Where(t => t.Type.Equals("Debt Payment", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));
                Debug.WriteLine($"TotalDebtPayments calculated: {TotalDebtPayments}");

                // Update daily totals
                DailySales = TotalSales;
                DailyReturns = TotalReturns;
                DailyExpenses = TotalExpenses;
                SupplierPayments = TotalSupplierPayments;
                DebtPayments = TotalDebtPayments;

                // Calculate net values
                NetSales = TotalSales - TotalReturns;
                NetCashflow = TotalSales + TotalDebtPayments - (TotalExpenses + TotalReturns);
                NetEarnings = NetSales - TotalExpenses;

                Debug.WriteLine($"Net calculations completed:");
                Debug.WriteLine($"NetSales: {NetSales}");
                Debug.WriteLine($"NetCashflow: {NetCashflow}");
                Debug.WriteLine($"NetEarnings: {NetEarnings}");

                // Update balance calculations
                if (CurrentDrawer != null)
                {
                    var calculatedBalance = CalculateCurrentBalance();
                    Debug.WriteLine($"Calculated balance: {calculatedBalance}");
                    Debug.WriteLine($"Current drawer balance: {CurrentDrawer.CurrentBalance}");

                    if (CurrentDrawer.CurrentBalance != calculatedBalance)
                    {
                        Debug.WriteLine($"Updating drawer balance from {CurrentDrawer.CurrentBalance} to {calculatedBalance}");
                        CurrentDrawer.CurrentBalance = calculatedBalance;
                        OnPropertyChanged(nameof(CurrentBalance));
                        OnPropertyChanged(nameof(ExpectedBalance));
                        OnPropertyChanged(nameof(Difference));
                    }
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NotifyTotalChanges();
                });

                Debug.WriteLine("LoadFinancialOverviewAsync completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadFinancialOverviewAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync("Error updating financial summary");
            }
        }
        public async Task ApplyDateFilterAsync()
        {
            try
            {
                IsProcessing = true;

                if (StartDate > EndDate)
                {
                    await ShowErrorMessageAsync("Start date cannot be after end date");
                    return;
                }

                // Load filtered transactions
                if (CurrentDrawer != null)
                {
                    var history = await _drawerService.GetDrawerHistoryAsync(CurrentDrawer.DrawerId);
                    var filteredHistory = history.Where(t =>
                        t.Timestamp.Date >= StartDate.Date &&
                        t.Timestamp.Date <= EndDate.Date)
                        .OrderByDescending(t => t.Timestamp);

                    DrawerHistory = new ObservableCollection<DrawerTransactionDTO>(filteredHistory);

                    // Recalculate financial totals based on filtered data
                    var transactions = DrawerHistory.Where(t =>
                        t.Timestamp.Date >= StartDate.Date &&
                        t.Timestamp.Date <= EndDate.Date);

                    // Calculate totals
                    TotalSales = transactions
                        .Where(t => t.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.Amount));

                    TotalReturns = transactions
                        .Where(t => t.Type.Equals("Return", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.Amount));

                    var regularExpenses = transactions
                        .Where(t => t.Type.Equals("Expense", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.Amount));

                    var supplierPayments = transactions
                        .Where(t => t.Type.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.Amount));

                    TotalExpenses = regularExpenses + supplierPayments;
                    TotalSupplierPayments = supplierPayments;

                    TotalDebtPayments = transactions
                        .Where(t => t.Type.Equals("Debt Payment", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.Amount));

                    // Update calculated totals
                    NetSales = TotalSales - TotalReturns;
                    NetCashflow = TotalSales + TotalDebtPayments - (TotalExpenses + TotalReturns);
                    NetEarnings = NetSales - TotalExpenses;

                    // Update the UI
                    UpdateStatus();
                    NotifyTotalChanges();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying date filter: {ex.Message}");
                await ShowErrorMessageAsync($"Error applying date filter: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }
        private decimal CalculateCurrentBalance()
        {
            if (DrawerHistory == null || !DrawerHistory.Any())
                return 0;

            decimal balance = 0;
            foreach (var transaction in DrawerHistory.OrderBy(t => t.Timestamp))
            {
                if (transaction.Type.Equals("Open", StringComparison.OrdinalIgnoreCase))
                {
                    balance = transaction.Amount;
                    continue;
                }

                switch (transaction.Type.ToLower())
                {
                    case "cash sale":
                    case "debt payment":
                    case "cash in":
                        balance += Math.Abs(transaction.Amount);
                        break;

                    case "expense":
                    case "supplier payment":
                    case "return":
                    case "cash out":
                    case "salary withdrawal":
                        balance -= Math.Abs(transaction.Amount);
                        break;
                }
            }

            return balance;
        }

        private void UpdateTotals()
        {
            if (DrawerHistory == null) return;

            var todayTransactions = DrawerHistory
                .Where(t => t.Timestamp.Date == DateTime.Today)
                .ToList();

            // Calculate base totals
            TotalSales = todayTransactions
                .Where(t => t.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));

            TotalReturns = todayTransactions
                .Where(t => t.Type.Equals("Return", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));

            var regularExpenses = todayTransactions
                .Where(t => t.Type.Equals("Expense", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));

            var supplierPayments = todayTransactions
                .Where(t => t.Type.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));

            TotalExpenses = regularExpenses + supplierPayments;
            TotalSupplierPayments = supplierPayments;

            TotalDebtPayments = todayTransactions
                .Where(t => t.Type.Equals("Debt Payment", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));

            // Update daily totals
            DailySales = TotalSales;
            DailyReturns = TotalReturns;
            DailyExpenses = TotalExpenses;
            SupplierPayments = TotalSupplierPayments;
            DebtPayments = TotalDebtPayments;

            // Calculate net values
            NetSales = TotalSales - TotalReturns;
            NetCashflow = TotalSales + TotalDebtPayments - (TotalExpenses + TotalReturns);
            NetEarnings = NetSales - TotalExpenses;

            NotifyTotalChanges();
        }

        private void NotifyTotalChanges()
        {
            OnPropertyChanged(nameof(TotalSales));
            OnPropertyChanged(nameof(TotalReturns));
            OnPropertyChanged(nameof(TotalExpenses));
            OnPropertyChanged(nameof(TotalDebtPayments));
            OnPropertyChanged(nameof(TotalSupplierPayments));
            OnPropertyChanged(nameof(DailySales));
            OnPropertyChanged(nameof(DailyReturns));
            OnPropertyChanged(nameof(DailyExpenses));
            OnPropertyChanged(nameof(DebtPayments));
            OnPropertyChanged(nameof(SupplierPayments));
            OnPropertyChanged(nameof(NetSales));
            OnPropertyChanged(nameof(NetCashflow));
            OnPropertyChanged(nameof(NetEarnings));
            OnPropertyChanged(nameof(CurrentBalance));
            OnPropertyChanged(nameof(ExpectedBalance));
            OnPropertyChanged(nameof(Difference));
        }

        private void ResetFinancialTotals()
        {
            TotalSales = 0;
            TotalReturns = 0;
            TotalExpenses = 0;
            TotalDebtPayments = 0;
            TotalSupplierPayments = 0;
            DailySales = 0;
            DailyReturns = 0;
            DailyExpenses = 0;
            NetSales = 0;
            NetCashflow = 0;
            NetEarnings = 0;
        }

        private async Task UpdateFinancialSummaryAsync()
        {
            try
            {
                var todayTransactions = DrawerHistory
                    .Where(t => t.Timestamp.Date == DateTime.Today)
                    .ToList();

                // Calculate base totals
                TotalSales = todayTransactions
                    .Where(t => t.Type.Equals("Cash Sale", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));

                TotalReturns = todayTransactions
                    .Where(t => t.Type.Equals("Return", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));

                var regularExpenses = todayTransactions
                    .Where(t => t.Type.Equals("Expense", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));

                var supplierPayments = todayTransactions
                    .Where(t => t.Type.Equals("Supplier Payment", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));

                TotalExpenses = regularExpenses + supplierPayments;
                TotalSupplierPayments = supplierPayments;

                TotalDebtPayments = todayTransactions
                    .Where(t => t.Type.Equals("Debt Payment", StringComparison.OrdinalIgnoreCase))
                    .Sum(t => Math.Abs(t.Amount));

                // Update UI properties
                OnPropertyChanged(nameof(TotalSales));
                OnPropertyChanged(nameof(TotalReturns));
                OnPropertyChanged(nameof(TotalExpenses));
                OnPropertyChanged(nameof(TotalSupplierPayments));
                OnPropertyChanged(nameof(TotalDebtPayments));
                OnPropertyChanged(nameof(NetSales));
                OnPropertyChanged(nameof(NetCashflow));
                OnPropertyChanged(nameof(NetEarnings));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating financial summary: {ex.Message}");
                await ShowErrorMessageAsync("Error updating financial summary");
            }
        }
    }
}