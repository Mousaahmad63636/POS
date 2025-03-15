using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IQuoteService _quoteService;
        private readonly ITransactionService _transactionService;
        private readonly ICustomerDebtService _customerDebtService;
        private readonly IDrawerService _drawerService;
        private readonly IProductService _productService;
        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);
        private bool _isDisposed;

        private decimal _todaySales;
        private decimal _outstandingDebt;
        private decimal _cashInDrawer;
        private decimal _monthlyRevenue;
        private decimal _netProfit;
        private int _pendingQuotes;
        private int _lowStockCount;
        private bool _isLoading;
        private bool _hasErrors;
        private string _errorMessage = string.Empty;

        public decimal TodaySales
        {
            get => _todaySales;
            set => SetProperty(ref _todaySales, value);
        }

        public decimal OutstandingDebt
        {
            get => _outstandingDebt;
            set => SetProperty(ref _outstandingDebt, value);
        }

        public decimal CashInDrawer
        {
            get => _cashInDrawer;
            set => SetProperty(ref _cashInDrawer, value);
        }

        public decimal MonthlyRevenue
        {
            get => _monthlyRevenue;
            set => SetProperty(ref _monthlyRevenue, value);
        }

        public decimal NetProfit
        {
            get => _netProfit;
            set => SetProperty(ref _netProfit, value);
        }

        public int PendingQuotes
        {
            get => _pendingQuotes;
            set => SetProperty(ref _pendingQuotes, value);
        }

        public int LowStockCount
        {
            get => _lowStockCount;
            set => SetProperty(ref _lowStockCount, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasErrors
        {
            get => _hasErrors;
            set => SetProperty(ref _hasErrors, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public ICommand NavigateCommand { get; }
        public ICommand RefreshCommand { get; }

        public DashboardViewModel(
            IServiceProvider serviceProvider,
            IQuoteService quoteService,
            ITransactionService transactionService,
            ICustomerDebtService customerDebtService,
            IDrawerService drawerService,
            IProductService productService,
            IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _serviceProvider = serviceProvider;
            _quoteService = quoteService;
            _transactionService = transactionService;
            _customerDebtService = customerDebtService;
            _drawerService = drawerService;
            _productService = productService;

            NavigateCommand = new RelayCommand(ExecuteNavigation);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync());

            _ = LoadDataAsync();
        }

        private async Task RefreshDataAsync()
        {
            await LoadDataAsync();
        }

        protected override async Task LoadDataAsync()
        {
            if (!await _loadingSemaphore.WaitAsync(0))
            {
                Debug.WriteLine("LoadDataAsync skipped - already in progress");
                return;
            }

            try
            {
                IsLoading = true;
                HasErrors = false;
                ErrorMessage = string.Empty;

                await LoadFinancialDataAsync();
                await LoadQuotesDataAsync();
                await LoadInventoryDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading dashboard data: {ex}");
                HasErrors = true;
                ErrorMessage = "Failed to load dashboard data. Please try refreshing.";
            }
            finally
            {
                IsLoading = false;
                _loadingSemaphore.Release();
            }
        }

        private async Task LoadFinancialDataAsync()
        {
            try
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1); // Get tomorrow's date
                var startOfMonth = new DateTime(today.Year, today.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

                // Get transactions for today (from 00:00:00 to 23:59:59)
                var todaysSummary = await _transactionService.GetTransactionSummaryByDateRangeAsync(today, tomorrow);
                TodaySales = todaysSummary.TotalSales;

                // Get rest of financial data
                OutstandingDebt = await _customerDebtService.GetTotalOutstandingDebtAsync();
                CashInDrawer = await _drawerService.GetCurrentBalanceAsync();
                MonthlyRevenue = await _transactionService.GetTotalSalesAsync(startOfMonth, endOfMonth);
                NetProfit = todaysSummary.TotalSales - todaysSummary.TotalReturns;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadFinancialDataAsync: {ex}");

                // Set values to 0 in case of error
                TodaySales = 0;
                OutstandingDebt = 0;
                CashInDrawer = 0;
                MonthlyRevenue = 0;
                NetProfit = 0;

                throw;
            }
        }

        private async Task LoadQuotesDataAsync()
        {
            try
            {
                var quotes = await _quoteService.GetPendingQuotes();
                PendingQuotes = quotes.Count();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadQuotesDataAsync: {ex}");
                PendingQuotes = 0;
                throw;
            }
        }

        private async Task LoadInventoryDataAsync()
        {
            try
            {
                var lowStockProducts = await _productService.GetLowStockProductsAsync();
                LowStockCount = lowStockProducts.Count();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadInventoryDataAsync: {ex}");
                LowStockCount = 0;
                throw;
            }
        }

        private void ExecuteNavigation(object? parameter)
        {
            try
            {
                if (parameter is not string destination)
                {
                    Debug.WriteLine("Invalid navigation parameter");
                    ShowTemporaryErrorMessage("Invalid navigation destination");
                    return;
                }

                var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                mainViewModel.NavigateCommand.Execute(destination);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation error: {ex}");
                ShowTemporaryErrorMessage($"Navigation failed: {ex.Message}");
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(async _ =>
                await SafeLoadAsync(LoadFinancialDataAsync));

            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(async _ =>
                await SafeLoadAsync(LoadFinancialDataAsync));

            _eventAggregator.Subscribe<EntityChangedEvent<QuoteDTO>>(async _ =>
                await SafeLoadAsync(LoadQuotesDataAsync));

            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(async _ =>
                await SafeLoadAsync(LoadInventoryDataAsync));

            _eventAggregator.Subscribe<DrawerUpdateEvent>(async _ =>
                await SafeLoadAsync(LoadFinancialDataAsync));
        }

        protected override void UnsubscribeFromEvents()
        {
            // Event handlers unsubscribed in base.Dispose()
        }

        private async Task SafeLoadAsync(Func<Task> loadingTask)
        {
            if (!await _loadingSemaphore.WaitAsync(0))
            {
                return;
            }

            try
            {
                await loadingTask();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in event handler: {ex}");
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            ErrorMessage = message;
            HasErrors = true;

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message) // Only clear if still the same message
                    {
                        ErrorMessage = string.Empty;
                        HasErrors = false;
                    }
                });
            });
        }

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                _loadingSemaphore?.Dispose();
                _isDisposed = true;
            }

            base.Dispose();
        }
    }
}