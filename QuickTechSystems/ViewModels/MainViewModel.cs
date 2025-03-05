using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Helpers;
using QuickTechSystems.ViewModels;
using QuickTechSystems.WPF.Commands;

using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LanguageManager _languageManager;
        private ViewModelBase? _currentViewModel;
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;

        public ViewModelBase? CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public FlowDirection CurrentFlowDirection
        {
            get => _currentFlowDirection;
            set => SetProperty(ref _currentFlowDirection, value);
        }

        public ICommand NavigateCommand { get; }

        public MainViewModel(
            IServiceProvider serviceProvider,
            IEventAggregator eventAggregator,
            LanguageManager languageManager) : base(eventAggregator)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _languageManager = languageManager ?? throw new ArgumentNullException(nameof(languageManager));

            NavigateCommand = new RelayCommand(ExecuteNavigation);

            // Subscribe to language changes
            _languageManager.LanguageChanged += OnLanguageChanged;

            // Change initial view to Dashboard
            ExecuteNavigation("Dashboard");
        }

        private void OnLanguageChanged(object? sender, string languageCode)
        {
            CurrentFlowDirection = _languageManager.GetFlowDirection(languageCode);

            // Refresh current view if needed
            if (CurrentViewModel != null)
            {
                var currentDestination = CurrentViewModel.GetType().Name.Replace("ViewModel", "");
                ExecuteNavigation(currentDestination);
            }
        }

        private void ExecuteNavigation(object? parameter)
        {
            try
            {
                if (parameter is not string destination)
                {
                    Debug.WriteLine($"Invalid navigation parameter: {parameter}");
                    return;
                }

                Debug.WriteLine($"Attempting to navigate to: {destination}");

                CurrentViewModel = destination switch
                {
                    "Dashboard" => _serviceProvider.GetRequiredService<DashboardViewModel>(),
                    "Products" => _serviceProvider.GetRequiredService<ProductViewModel>(),
                    "Categories" => _serviceProvider.GetRequiredService<CategoryViewModel>(),
                    "Customers" => _serviceProvider.GetRequiredService<CustomerViewModel>(),
                    "Transactions" => _serviceProvider.GetRequiredService<TransactionViewModel>(),
                    "Settings" => _serviceProvider.GetRequiredService<SettingsViewModel>(),
                    "Suppliers" => _serviceProvider.GetRequiredService<SupplierViewModel>(),
                    "TransactionHistory" => _serviceProvider.GetRequiredService<TransactionHistoryViewModel>(),
                    "Profit" => _serviceProvider.GetRequiredService<ProfitViewModel>(),
                    "Expenses" => _serviceProvider.GetRequiredService<ExpenseViewModel>(),
                    "Drawer" => _serviceProvider.GetRequiredService<DrawerViewModel>(),
                    "CustomerDebt" => _serviceProvider.GetRequiredService<CustomerDebtViewModel>(),
                    "Employees" => _serviceProvider.GetRequiredService<EmployeeViewModel>(),
                    "MonthlySubscriptions" => _serviceProvider.GetRequiredService<MonthlySubscriptionViewModel>(),
                    _ => throw new ArgumentException($"Unknown destination: {destination}")
                };

                Debug.WriteLine($"Successfully navigated to: {destination}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation error: {ex.Message}");
                MessageBox.Show($"Error navigating to requested page: {ex.Message}",
                              "Navigation Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _languageManager.LanguageChanged -= OnLanguageChanged;
            }
            base.Dispose(disposing);
        }

        protected override Task LoadDataAsync()
        {
            return Task.CompletedTask;
        }
    }
}