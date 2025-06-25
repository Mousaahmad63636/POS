using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using QuickTechSystems.Commands;
using QuickTechSystems.ViewModels.Customer;
using QuickTechSystems.ViewModels.Product;
using QuickTechSystems.ViewModels.Supplier;
using QuickTechSystems.ViewModels.Settings;
using QuickTechSystems.ViewModels.Categorie;
using QuickTechSystems.ViewModels.Employee;
using QuickTechSystems.ViewModels.Expense;
using QuickTechSystems.ViewModels.Restaurent;
using QuickTechSystems.ViewModels.Welcome;
using QuickTechSystems.ViewModels.Transaction;
using QuickTechSystems.ViewModels;

namespace QuickTechSystems.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LanguageManager _languageManager;
        private readonly UserRole _currentUserRole;
        private readonly EmployeeDTO _currentUser;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _navigationLock = new SemaphoreSlim(1, 1);
        private volatile bool _semaphoreAcquired = false;
        private readonly Dictionary<string, Func<ViewModelBase>> _viewModelFactory;
        private readonly Dictionary<string, ViewModelBase> _viewModelCache;
        private readonly HashSet<string> _permissionRequiredViews;
        private readonly Dictionary<UserRole, HashSet<string>> _rolePermissions;
        private readonly Queue<string> _navigationQueue = new Queue<string>();

        private ViewModelBase? _currentViewModel;
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;
        private bool _isSidebarCollapsed;
        private bool _isLoading;
        private string _loadingMessage = "Loading...";
        private string _errorMessage = string.Empty;
        private bool _isRestaurantMode;
        private string _pendingNavigation = string.Empty;

        private bool _isNavigationEnabled = true;
        private DateTime _lastNavigationTime = DateTime.MinValue;
        private const int NAVIGATION_COOLDOWN_MS = 150;
        private volatile bool _isInitializing = false;

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

        public bool IsRestaurantMode
        {
            get => _isRestaurantMode;
            set
            {
                if (SetProperty(ref _isRestaurantMode, value))
                {
                    Debug.WriteLine($"IsRestaurantMode changed to: {value}");
                }
            }
        }

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
            ISystemPreferencesService preferencesService)
            : base(eventAggregator)
        {
            _serviceProvider = serviceProvider;
            _languageManager = languageManager;

            _currentUser = (EmployeeDTO)App.Current.Properties["CurrentUser"];
            _currentUserRole = Enum.Parse<UserRole>(_currentUser.Role);
            Debug.WriteLine($"User logged in as: {_currentUser.Role} (Parsed Role: {_currentUserRole})");

            _viewModelCache = new Dictionary<string, ViewModelBase>();
            _viewModelFactory = new Dictionary<string, Func<ViewModelBase>>
            {
                ["Welcome"] = () => GetOrCreateViewModel("Welcome", () => _serviceProvider.GetRequiredService<WelcomeViewModel>()),
                ["Transactions"] = () => GetOrCreateViewModel("Transactions", () => _serviceProvider.GetRequiredService<WelcomeViewModel>()),
                ["Categories"] = () => GetOrCreateViewModel("Categories", () => _serviceProvider.GetRequiredService<CategoryViewModel>()),
                ["Customers"] = () => GetOrCreateViewModel("Customers", () => _serviceProvider.GetRequiredService<CustomerViewModel>()),
                ["Products"] = () => GetOrCreateViewModel("Products", () => _serviceProvider.GetRequiredService<ProductViewModel>()),
                ["Settings"] = () => GetOrCreateViewModel("Settings", () => _serviceProvider.GetRequiredService<SettingsViewModel>()),
                ["Suppliers"] = () => GetOrCreateViewModel("Suppliers", () => _serviceProvider.GetRequiredService<SupplierViewModel>()),
                ["Expenses"] = () => GetOrCreateViewModel("Expenses", () => _serviceProvider.GetRequiredService<ExpenseViewModel>()),
                ["Drawer"] = () => GetOrCreateViewModel("Drawer", () => _serviceProvider.GetRequiredService<DrawerViewModel>()),
                ["Employees"] = () => GetOrCreateViewModel("Employees", () => _serviceProvider.GetRequiredService<EmployeeViewModel>()),
                ["TableManagement"] = () => GetOrCreateViewModel("TableManagement", () => _serviceProvider.GetRequiredService<TableManagementViewModel>()),
                ["TransactionHistory"] = () => GetOrCreateViewModel("TransactionHistory", () => _serviceProvider.GetRequiredService<TransactionHistoryViewModel>())
            };

            _permissionRequiredViews = new HashSet<string>
            {
                "Products", "Categories", "Suppliers", "Expenses", "Settings",
                "Employees", "MonthlySubscriptions", "TransactionHistory",
                "Profit", "LowStockHistory", "TableManagement"
            };

            _rolePermissions = new Dictionary<UserRole, HashSet<string>>
            {
                [UserRole.Admin] = new HashSet<string>
                {
                    "Welcome", "Transactions", "Products", "Categories", "Suppliers",
                    "Expenses", "Settings", "Employees", "MonthlySubscriptions",
                    "TransactionHistory", "Profit", "Customers", "Drawer",
                    "LowStockHistory", "TableManagement"
                },
                [UserRole.Manager] = new HashSet<string>
                {
                    "Welcome", "Transactions", "Products", "Categories", "Suppliers",
                    "Expenses", "MonthlySubscriptions", "TransactionHistory",
                    "Profit", "Customers", "Drawer", "LowStockHistory", "TableManagement"
                },
                [UserRole.Cashier] = new HashSet<string>
                {
                    "Welcome", "Transactions", "Customers", "Drawer"
                }
            };

            _eventAggregator.Subscribe<ApplicationModeChangedEvent>(OnApplicationModeChanged);
            Debug.WriteLine("Subscribed to ApplicationModeChangedEvent");

            NavigateCommand = new RelayCommand(ExecuteNavigation, CanExecuteNavigation);
            LogoutCommand = new RelayCommand(ExecuteLogout);
            ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
            RefreshCommand = new RelayCommand(async _ => await RefreshCurrentView());

            _languageManager.LanguageChanged += OnLanguageChanged;

            Task.Run(async () => {
                try
                {
                    var restaurantModeStr = await preferencesService.GetPreferenceValueAsync("default", "RestaurantMode", "false");
                    bool restaurantMode = bool.Parse(restaurantModeStr);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        Debug.WriteLine($"Setting IsRestaurantMode to {restaurantMode}");
                        IsRestaurantMode = restaurantMode;
                        Debug.WriteLine($"Restaurant mode loaded from preferences: {restaurantMode}");
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading restaurant mode setting: {ex.Message}");
                }
            });

            InitializeAsync();
        }

        private ViewModelBase GetOrCreateViewModel(string key, Func<ViewModelBase> factory)
        {
            if (!_viewModelCache.ContainsKey(key))
            {
                _viewModelCache[key] = factory();
            }
            return _viewModelCache[key];
        }

        private void OnApplicationModeChanged(ApplicationModeChangedEvent evt)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                Debug.WriteLine($"Restaurant mode event received: {evt.IsRestaurantMode}");
                IsRestaurantMode = evt.IsRestaurantMode;
            });
        }

        private async void InitializeAsync()
        {
            if (_isInitializing) return;

            bool lockAcquired = false;
            try
            {
                lockAcquired = await _operationLock.WaitAsync(0);
                if (!lockAcquired)
                {
                    Debug.WriteLine("Initialization already in progress");
                    return;
                }

                _semaphoreAcquired = true;
                _isInitializing = true;
                IsLoading = true;
                LoadingMessage = "Initializing...";
                await Task.Delay(50);

                ((App)System.Windows.Application.Current).ApplyRestaurantModeSetting();

                await Task.Delay(100);

                _isInitializing = false;
                IsNavigationEnabled = true;

                await ExecuteInitialNavigationAsync(GetInitialView());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization error: {ex}");
                await HandleExceptionAsync("Error initializing application", ex);
            }
            finally
            {
                IsLoading = false;
                _isInitializing = false;

                if (lockAcquired && _semaphoreAcquired)
                {
                    _semaphoreAcquired = false;
                    _operationLock.Release();
                }
            }
        }

        private string GetInitialView()
        {
            return _currentUserRole switch
            {
                UserRole.Cashier => "Welcome",
                _ => "Welcome"
            };
        }

        private bool CanExecuteNavigation(object? parameter)
        {
            if (parameter is not string destination || !IsNavigationEnabled)
                return false;

            if ((DateTime.Now - _lastNavigationTime).TotalMilliseconds < NAVIGATION_COOLDOWN_MS)
                return false;

            if (_currentUserRole == UserRole.Admin)
                return _viewModelFactory.ContainsKey(destination);

            return _rolePermissions.ContainsKey(_currentUserRole) &&
                   _rolePermissions[_currentUserRole].Contains(destination) &&
                   _viewModelFactory.ContainsKey(destination);
        }

        private async Task ExecuteInitialNavigationAsync(string destination)
        {
            if (_viewModelFactory.TryGetValue(destination, out var factory))
            {
                CurrentViewModel = factory();
                await Task.Delay(25);
                await CurrentViewModel.LoadAsync();
            }
        }

        private async void ExecuteNavigation(object? parameter)
        {
            if (parameter is not string destination)
            {
                Debug.WriteLine($"Invalid navigation parameter: {parameter}");
                return;
            }

            if (!CanExecuteNavigation(destination))
            {
                string errorMessage = (DateTime.Now - _lastNavigationTime).TotalMilliseconds < NAVIGATION_COOLDOWN_MS
                    ? ""
                    : "You don't have permission to access this feature.";

                if (!string.IsNullOrEmpty(errorMessage))
                    ShowTemporaryErrorMessage(errorMessage);
                return;
            }

            bool navigationLockAcquired = false;
            try
            {
                navigationLockAcquired = await _navigationLock.WaitAsync(0);
                if (!navigationLockAcquired)
                {
                    _pendingNavigation = destination;
                    IsLoading = true;
                    LoadingMessage = $"Preparing {destination}...";

                    await Task.Delay(50);
                    if (_pendingNavigation == destination)
                    {
                        await ExecuteNavigationInternal(destination);
                    }
                    return;
                }

                await ExecuteNavigationInternal(destination);
            }
            finally
            {
                if (navigationLockAcquired)
                {
                    _navigationLock.Release();
                }
            }
        }

        private async Task ExecuteNavigationInternal(string destination)
        {
            try
            {
                IsNavigationEnabled = false;
                IsLoading = true;
                ErrorMessage = string.Empty;
                LoadingMessage = $"Loading {destination}...";
                _lastNavigationTime = DateTime.Now;
                _pendingNavigation = string.Empty;

                if (_viewModelFactory.TryGetValue(destination, out var factory))
                {
                    var targetViewModel = factory();

                    if (CurrentViewModel != targetViewModel)
                    {
                        CurrentViewModel = targetViewModel;
                        await Task.Delay(25);
                        await targetViewModel.LoadAsync();
                    }
                }
                else
                {
                    throw new ArgumentException($"Unknown destination: {destination}");
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync($"Error navigating to {destination}", ex);
            }
            finally
            {
                IsLoading = false;
                IsNavigationEnabled = true;

                if (_navigationQueue.Count > 0)
                {
                    var nextDestination = _navigationQueue.Dequeue();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(25);
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            ExecuteNavigation(nextDestination));
                    });
                }
            }
        }

        private async void ExecuteLogout(object? parameter)
        {
            if (!await _operationLock.WaitAsync(0))
            {
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

                foreach (var viewModel in _viewModelCache.Values)
                {
                    viewModel?.Dispose();
                }
                _viewModelCache.Clear();

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
                return;
            }

            try
            {
                if (CurrentViewModel != null)
                {
                    IsLoading = true;
                    ErrorMessage = string.Empty;
                    LoadingMessage = "Refreshing...";

                    await Task.Delay(25);
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

            if (ex.Message.Contains("A second operation was started") ||
                (ex.InnerException != null && ex.InnerException.Message.Contains("A second operation was started")))
            {
                ShowTemporaryErrorMessage("System busy. Retrying...");
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ShowTemporaryErrorMessage("Connection lost. Please retry.");
            }
            else if (ex.Message.Contains("DbContext") || ex.Message.Contains("Entity Framework") ||
                    (ex.InnerException != null && (ex.InnerException.Message.Contains("DbContext") ||
                     ex.InnerException.Message.Contains("Entity Framework"))))
            {
                ShowTemporaryErrorMessage("Database busy. Please retry.");
            }
            else
            {
                ShowTemporaryErrorMessage("Error occurred. Please retry.");
            }
        }

        private void ShowTemporaryErrorMessage(string message)
        {
            ErrorMessage = message;

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message)
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
                foreach (var viewModel in _viewModelCache.Values)
                {
                    viewModel?.Dispose();
                }
                _viewModelCache.Clear();
                CurrentViewModel?.Dispose();
                _operationLock?.Dispose();
                _navigationLock?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}