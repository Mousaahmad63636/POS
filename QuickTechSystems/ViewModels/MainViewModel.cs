using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Helpers;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Threading.Tasks;
using System.Threading;

namespace QuickTechSystems.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LanguageManager _languageManager;
        private readonly IActivityLogger _activityLogger;
        private readonly UserRole _currentUserRole;
        private readonly EmployeeDTO _currentUser;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        private ViewModelBase? _currentViewModel;
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;
        private bool _isSidebarCollapsed;
        private bool _isLoading;
        private string _loadingMessage = "Loading...";
        private string _errorMessage = string.Empty;

        private bool _isNavigationEnabled = true;
        private DateTime _lastNavigationTime = DateTime.MinValue;
        private const int NAVIGATION_COOLDOWN_MS = 500; // Consider making this configurable if needed

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

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set => SetProperty(ref _isSidebarCollapsed, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsNavigationEnabled
        {
            get => _isNavigationEnabled;
            set => SetProperty(ref _isNavigationEnabled, value);
        }

        // Role-based visibility properties
        public bool IsAdmin => _currentUserRole == UserRole.Admin;
        public bool IsManager => _currentUserRole == UserRole.Manager || IsAdmin;
        public bool IsCashier => true;

        public string CurrentUserName => $"{_currentUser.FirstName} {_currentUser.LastName}";
        public string CurrentUserRole => _currentUser.Role;
        public string UserInitials => $"{_currentUser.FirstName[0]}{_currentUser.LastName[0]}".ToUpper();

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand RefreshCommand { get; }

        public MainViewModel(
            IServiceProvider serviceProvider,
            IEventAggregator eventAggregator,
            LanguageManager languageManager,
            IActivityLogger activityLogger)
            : base(eventAggregator)
        {
            _serviceProvider = serviceProvider;
            _languageManager = languageManager;
            _activityLogger = activityLogger;

            _currentUser = (EmployeeDTO)App.Current.Properties["CurrentUser"];
            _currentUserRole = Enum.Parse<UserRole>(_currentUser.Role);
            Debug.WriteLine($"User logged in as: {_currentUser.Role} (Parsed Role: {_currentUserRole})"); // Debug log for role validation

            NavigateCommand = new RelayCommand(ExecuteNavigation, CanExecuteNavigation);
            LogoutCommand = new RelayCommand(ExecuteLogout);
            ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
            RefreshCommand = new RelayCommand(async _ => await RefreshCurrentView());

            _languageManager.LanguageChanged += OnLanguageChanged;

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Initializing...";
                await Task.Delay(100); // Prevent UI flicker
                ExecuteNavigation(GetInitialView());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization error: {ex}");
                await HandleExceptionAsync("Error initializing application", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetInitialView()
        {
            return _currentUserRole switch
            {
                UserRole.Admin => "Dashboard",
                UserRole.Manager => "Dashboard",
                UserRole.Cashier => "Transactions",
                _ => "Transactions"
            };
        }

        private bool CanExecuteNavigation(object? parameter)
        {
            // Admin bypass takes precedence
            if (_currentUserRole == UserRole.Admin)
                return true;

            if (parameter is not string destination || !IsNavigationEnabled)
                return false;

            if ((DateTime.Now - _lastNavigationTime).TotalMilliseconds < NAVIGATION_COOLDOWN_MS)
                return false;

            return destination switch
            {
                "Dashboard" => IsManager,
                "Transactions" => true,
                "CustomerDebt" => true,
                "Products" => IsManager,
                "Categories" => IsManager,
                "Suppliers" => IsManager,
                "Expenses" => IsManager,
                "Settings" => IsAdmin,
                "Employees" => IsAdmin,
                "MonthlySubscriptions" => IsManager,
                "Quotes" => true,
                "Drawer" => true,
                "TransactionHistory" => IsManager,
                "Profit" => IsManager,
                "Customers" => IsManager,
                "LowStockHistory" => IsManager,
                _ => false
            };
        }

        private async void ExecuteNavigation(object? parameter)
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Navigation operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (parameter is not string destination)
                {
                    Debug.WriteLine($"Invalid navigation parameter: {parameter}");
                    return;
                }

                if (!CanExecuteNavigation(destination))
                {
                    string errorMessage = (DateTime.Now - _lastNavigationTime).TotalMilliseconds < NAVIGATION_COOLDOWN_MS
                        ? "Please wait a moment before navigating again (cooldown active)."
                        : "You don't have permission to access this feature.";
                    ShowTemporaryErrorMessage(errorMessage);
                    return;
                }

                IsNavigationEnabled = false;
                IsLoading = true;
                ErrorMessage = string.Empty;
                LoadingMessage = $"Loading {destination}...";
                _lastNavigationTime = DateTime.Now;

                // Fire-and-forget activity logging to prevent DbContext conflicts
                Task.Run(async () => {
                    try
                    {
                        await _activityLogger.LogActivityAsync(
                            _currentUser.Username,
                            "Navigation",
                            $"Navigated to {destination}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error logging activity: {ex.Message}");
                    }
                });

                try
                {
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
                        "Quotes" => _serviceProvider.GetRequiredService<QuoteViewModel>(),
                        "LowStockHistory" => _serviceProvider.GetRequiredService<LowStockHistoryViewModel>(),
                        _ => throw new ArgumentException($"Unknown destination: {destination}")
                    };

                    if (CurrentViewModel != null)
                    {
                        await CurrentViewModel.LoadAsync();
                    }
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync($"Error navigating to {destination}", ex);
                }
            }
            finally
            {
                IsLoading = false;
                IsNavigationEnabled = true;
                _operationLock.Release();
            }
        }

        private async void ExecuteLogout(object? parameter)
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Logout operation already in progress. Please wait.");
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    "Are you sure you want to logout?",
                    "Confirm Logout",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result != MessageBoxResult.Yes)
                    return;

                IsLoading = true;
                ErrorMessage = string.Empty;
                LoadingMessage = "Logging out...";

                await _activityLogger.LogActivityAsync(
                    _currentUser.Username,
                    "Logout",
                    "User logged out"
                );

                App.Current.Properties.Remove("CurrentUser");
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Logout error", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private void OnLanguageChanged(object? sender, string languageCode)
        {
            CurrentFlowDirection = _languageManager.GetFlowDirection(languageCode);

            if (CurrentViewModel != null)
            {
                var currentDestination = CurrentViewModel.GetType().Name.Replace("ViewModel", "");
                ExecuteNavigation(currentDestination);
            }
        }

        private async Task RefreshCurrentView()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Refresh operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (CurrentViewModel != null)
                {
                    IsLoading = true;
                    ErrorMessage = string.Empty;
                    LoadingMessage = "Refreshing...";

                    // Use LoadAsync instead of LoadDataAsync (which is protected)
                    await CurrentViewModel.LoadAsync();
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error refreshing view", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task HandleExceptionAsync(string context, Exception ex)
        {
            Debug.WriteLine($"{context}: {ex}");

            // Special handling for known database errors
            if (ex.Message.Contains("A second operation was started") ||
                (ex.InnerException != null && ex.InnerException.Message.Contains("A second operation was started")))
            {
                ShowTemporaryErrorMessage("System is busy. Please try again in a moment.");
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ShowTemporaryErrorMessage("Database connection lost. Please check your connection and try again.");
            }
            else if (ex.Message.Contains("DbContext") || ex.Message.Contains("Entity Framework") ||
                    (ex.InnerException != null && (ex.InnerException.Message.Contains("DbContext") ||
                     ex.InnerException.Message.Contains("Entity Framework"))))
            {
                ShowTemporaryErrorMessage("Database operation error. Please try again or restart the application.");
            }
            else
            {
                ShowTemporaryErrorMessage($"An error occurred. Please try again.");

                await _activityLogger.LogActivityAsync(
                    _currentUser.Username,
                    "Application Error",
                    context,
                    false,
                    ex.Message
                );
            }
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            ErrorMessage = message;

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message) // Only clear if still the same message
                    {
                        ErrorMessage = string.Empty;
                    }
                });
            });
        }

        protected override async Task LoadDataAsync()
        {
            await Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _languageManager.LanguageChanged -= OnLanguageChanged;
                CurrentViewModel?.Dispose();
                _operationLock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}