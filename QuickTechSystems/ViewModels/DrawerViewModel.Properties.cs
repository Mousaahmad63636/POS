using System;
using System.Collections.ObjectModel;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class DrawerViewModel
    {
        private DrawerDTO? _currentDrawer;
        private ObservableCollection<DrawerTransactionDTO> _drawerHistory;
        private string _statusMessage = string.Empty;
        private bool _isProcessing;

        public decimal CurrentBalance => CurrentDrawer?.CurrentBalance ?? 0;

        public decimal ExpectedBalance => CurrentDrawer?.ExpectedBalance ?? 0;

        public decimal Difference => CurrentDrawer?.Difference ?? 0;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private DateTime _minimumDate = DateTime.Today.AddYears(-1);
        private DateTime _maximumDate = DateTime.Today;

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    // Ensure EndDate is not before StartDate
                    if (EndDate < value)
                    {
                        EndDate = value;
                    }
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        public DateTime MinimumDate
        {
            get => _minimumDate;
            set => SetProperty(ref _minimumDate, value);
        }

        public DateTime MaximumDate
        {
            get => _maximumDate;
            set => SetProperty(ref _maximumDate, value);
        }

        public ICommand ApplyDateFilterCommand { get; }
        public decimal TotalSales
        {
            get => _totalSales;
            set => SetProperty(ref _totalSales, value);
        }

        public decimal TotalExpenses
        {
            get => _totalExpenses;
            set => SetProperty(ref _totalExpenses, value);
        }

        public decimal TotalDebtPayments
        {
            get => _totalDebtPayments;
            set => SetProperty(ref _totalDebtPayments, value);
        }

        public decimal TotalSupplierPayments
        {
            get => _totalSupplierPayments;
            set => SetProperty(ref _totalSupplierPayments, value);
        }

        public decimal NetEarnings
        {
            get => _netEarnings;
            set => SetProperty(ref _netEarnings, value);
        }

        public decimal TotalReturns
        {
            get => _totalReturns;
            set => SetProperty(ref _totalReturns, value);
        }

        public decimal NetSales
        {
            get => _netSales;
            set => SetProperty(ref _netSales, value);
        }

        public decimal NetCashflow
        {
            get => _netCashflow;
            set => SetProperty(ref _netCashflow, value);
        }

        public decimal DailySales
        {
            get => _dailySales;
            set => SetProperty(ref _dailySales, value);
        }

        public decimal DailyReturns
        {
            get => _dailyReturns;
            set => SetProperty(ref _dailyReturns, value);
        }

        public decimal DailyExpenses
        {
            get => _dailyExpenses;
            set => SetProperty(ref _dailyExpenses, value);
        }

        public decimal SupplierPayments
        {
            get => _supplierPayments;
            set => SetProperty(ref _supplierPayments, value);
        }

        public decimal DebtPayments
        {
            get => _debtPayments;
            set => SetProperty(ref _debtPayments, value);
        }

        public DrawerDTO? CurrentDrawer
        {
            get => _currentDrawer;
            set
            {
                if (SetProperty(ref _currentDrawer, value))
                {
                    OnPropertyChanged(nameof(IsDrawerOpen));
                    OnPropertyChanged(nameof(CanOpenDrawer));
                    OnPropertyChanged(nameof(CurrentBalance));
                    OnPropertyChanged(nameof(ExpectedBalance));
                    OnPropertyChanged(nameof(Difference));
                }
            }
        }

        public ObservableCollection<DrawerTransactionDTO> DrawerHistory
        {
            get => _drawerHistory;
            set => SetProperty(ref _drawerHistory, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsDrawerOpen => CurrentDrawer?.Status == "Open";
        public bool CanOpenDrawer => CurrentDrawer == null || CurrentDrawer.Status == "Closed";
    }
}