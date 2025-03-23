using System;
using System.Collections.ObjectModel;
using System.Security.Policy;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.IdentityModel.Tokens;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;
using static System.Runtime.InteropServices.JavaScript.JSType;
using ZXing.QrCode.Internal;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Application.Helpers;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel : ViewModelBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITransactionService _transactionService;
        private readonly ICustomerService _customerService;
        private readonly IProductService _productService;
        private readonly IDrawerService _drawerService;
        private readonly IQuoteService _quoteService;
        private readonly Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;
        private Dictionary<int, decimal> _customerSpecificPrices = new();
        private readonly ICategoryService _categoryService;
        private readonly ISystemPreferencesService _systemPreferencesService;
        public string CashPaymentButtonText => IsEditingTransaction ? "Update Transaction" : "Cash Payment";
        public string CustomerBalanceButtonText => IsEditingTransaction ? "Update Customer Balance" : "Add to Customer Balance";
        public Visibility EditModeIndicatorVisibility =>
            IsEditingTransaction ? Visibility.Visible : Visibility.Collapsed;

        // Thread synchronization
        private readonly SemaphoreSlim _transactionOperationLock = new SemaphoreSlim(1, 1);
        private bool _operationInProgress = false;
        private readonly Dictionary<string, SemaphoreSlim> _resourceLocks = new Dictionary<string, SemaphoreSlim>
        {
            { "CustomerSearch", new SemaphoreSlim(1, 1) },
            { "ProductSearch", new SemaphoreSlim(1, 1) },
            { "PrintOperation", new SemaphoreSlim(1, 1) },
            { "PaymentProcess", new SemaphoreSlim(1, 1) }
        };

        // Create a static resource cleanup registry to ensure all semaphores get properly disposed
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        // Cancellation token source for handling cancellations
        private CancellationTokenSource _globalCancellationTokenSource = new CancellationTokenSource();

        public Dictionary<int, decimal> CustomerSpecificPrices
        {
            get => _customerSpecificPrices;
            private set
            {
                _customerSpecificPrices = value;
                OnPropertyChanged(nameof(CustomerSpecificPrices));
            }
        }

        public TransactionViewModel(
            IUnitOfWork unitOfWork,
            ITransactionService transactionService,
            ICustomerService customerService,
            IProductService productService,
            IDrawerService drawerService,
            IQuoteService quoteService,
            ICategoryService categoryService,
            IBusinessSettingsService businessSettingsService,
            ISystemPreferencesService systemPreferencesService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _quoteService = quoteService ?? throw new ArgumentNullException(nameof(quoteService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _systemPreferencesService = systemPreferencesService ?? throw new ArgumentNullException(nameof(systemPreferencesService));
            _transactionChangedHandler = HandleTransactionChanged;
            _productChangedHandler = HandleProductChanged;

            try
            {
                // Subscribe to event aggregator events
                _eventAggregator.Subscribe<ApplicationModeChangedEvent>(OnApplicationModeChanged);

                InitializeCommands();
                InitializeCollections();
                StartNewTransaction();

                // Initialize data with proper async pattern
                _ = InitializeAsync(businessSettingsService);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TransactionViewModel constructor: {ex.Message}");
                StatusMessage = "Error initializing view model";
            }
        }

        // Proper async initialization to replace direct calls in constructor
        private async Task InitializeAsync(IBusinessSettingsService businessSettingsService)
        {
            try
            {
                // Initialize tasks in parallel
                var dataLoadTask = LoadDataAsync();
                var exchangeRateTask = LoadExchangeRate(businessSettingsService);
                var restaurantModeTask = LoadRestaurantModePreference();

                // Wait for all tasks to complete
                await Task.WhenAll(dataLoadTask, exchangeRateTask, restaurantModeTask);

                // Setup UI refresh timer for date/time
                SetupDateTimeRefreshTimer();

                // Register commands that require async initialization
                CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync());
                ProcessBarcodeCommand = new AsyncRelayCommand(async _ => await ProcessBarcodeInput());

                // Update status after successful initialization
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializeAsync: {ex.Message}");
                StatusMessage = "Error during initialization";
            }
        }

        // Setup timer with proper disposal management
        private void SetupDateTimeRefreshTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => CurrentDate = DateTime.Now;
            timer.Start();

            // Store timer in application properties for proper cleanup
            if (App.Current.Properties.Contains("TransactionViewModelTimer"))
            {
                var oldTimer = App.Current.Properties["TransactionViewModelTimer"] as System.Windows.Threading.DispatcherTimer;
                oldTimer?.Stop();
            }
            App.Current.Properties["TransactionViewModelTimer"] = timer;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Create a new cancellation for any ongoing operations
                try
                {
                    _globalCancellationTokenSource.Cancel();
                    _globalCancellationTokenSource.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cancelling operations: {ex.Message}");
                }

                // Unsubscribe from all events to prevent memory leaks
                try
                {
                    if (_eventAggregator != null)
                    {
                        _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
                        _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
                        _eventAggregator.Unsubscribe<ApplicationModeChangedEvent>(OnApplicationModeChanged);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error unsubscribing from events: {ex.Message}");
                }

                // Clean up timer resources
                CleanupLookupDebounceTimer();

                // Clean up UI refresh timer
                if (App.Current.Properties.Contains("TransactionViewModelTimer"))
                {
                    try
                    {
                        var timer = App.Current.Properties["TransactionViewModelTimer"] as System.Windows.Threading.DispatcherTimer;
                        timer?.Stop();
                        App.Current.Properties.Remove("TransactionViewModelTimer");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing timer: {ex.Message}");
                    }
                }

                // Dispose of semaphores
                try
                {
                    _transactionOperationLock?.Dispose();
                    _categoryLoadSemaphore?.Dispose();
                    _customerSearchSemaphore?.Dispose();

                    // Dispose resource locks
                    foreach (var lockItem in _resourceLocks.Values)
                    {
                        lockItem?.Dispose();
                    }
                    _resourceLocks.Clear();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing locks: {ex.Message}");
                }

                // Clean up collections to help with garbage collection
                try
                {
                    CleanupImageCache();
                    _customerSpecificPrices?.Clear();
                    _validationErrors?.Clear();

                    AllProducts?.Clear();
                    FilteredProducts?.Clear();
                    FilteredCustomers?.Clear();
                    HeldTransactions?.Clear();
                    ProductCategories?.Clear();

                    // Nullify large objects
                    _currentTransaction = null;
                    _selectedCustomer = null;
                    _selectedCustomerFromSearch = null;
                    _selectedSearchProduct = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error clearing collections: {ex.Message}");
                }

                // Cancel any pending operations
                IsLoading = false;

                // Log disposal for debugging
                Debug.WriteLine("TransactionViewModel disposed");
            }

            base.Dispose(disposing);
        }

        // Clean up image cache properly
        private void CleanupImageCache()
        {
            try
            {
                foreach (var key in _imageCache.Keys.ToList())
                {
                    _imageCache[key] = null;
                }
                _imageCache.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up image cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a database or critical operation safely with proper error handling and thread synchronization
        /// </summary>
        /// <typeparam name="T">The return type of the operation</typeparam>
        /// <param name="operation">The async operation to execute</param>
        /// <param name="operationName">A name for the operation (for logging)</param>
        /// <param name="resourceKey">Optional resource key if a specific resource lock should be used</param>
        /// <returns>The result of the operation</returns>
        private async Task<T> ExecuteOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Transaction operation", string resourceKey = null)
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            // Check if operation was cancelled globally
            if (_globalCancellationTokenSource.IsCancellationRequested)
            {
                Debug.WriteLine($"Operation {operationName} cancelled by global token");
                throw new OperationCanceledException();
            }

            // If an operation is already in progress, wait a bit
            int waitCount = 0;
            while (_operationInProgress && resourceKey == null)
            {
                waitCount++;
                Debug.WriteLine($"Operation in progress, waiting... (attempt {waitCount})");
                await Task.Delay(100);

                // Safety timeout
                if (waitCount > 50) // 5 seconds max wait
                {
                    Debug.WriteLine("TIMEOUT waiting for operation lock, proceeding anyway");
                    break;
                }
            }

            // Determine which lock to use
            SemaphoreSlim lockToUse = resourceKey != null && _resourceLocks.ContainsKey(resourceKey)
                ? _resourceLocks[resourceKey]
                : _transactionOperationLock;

            Debug.WriteLine($"Acquiring operation lock for: {operationName}");
            bool lockAcquired = false;

            try
            {
                // Try to get the lock with a timeout
                lockAcquired = await lockToUse.WaitAsync(TimeSpan.FromSeconds(10));
                if (!lockAcquired)
                {
                    Debug.WriteLine($"Timed out waiting for lock: {operationName}");
                    throw new TimeoutException($"Operation timed out while waiting for resource lock: {operationName}");
                }

                if (resourceKey == null)
                    _operationInProgress = true;

                Debug.WriteLine($"Executing operation: {operationName}");
                // Add a small delay to ensure any previous operation is fully complete
                await Task.Delay(50);
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (InvalidOperationException ex)
            {
                // Handle specific exception types with user-friendly messages
                Debug.WriteLine($"Operation error in {operationName}: {ex.Message}");
                await ShowErrorMessageAsync(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                // For all other exceptions, provide a more generic user-friendly message
                // but log the detailed exception for troubleshooting
                Debug.WriteLine($"ERROR in {operationName}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                string userMessage = "An unexpected error occurred. Please review your input and try again.";
                if (ex.Message.Contains("database") || ex.Message.Contains("SQL"))
                {
                    userMessage = "A database error occurred. The operation could not be completed.";
                }
                else if (ex.Message.Contains("network") || ex.Message.Contains("connection"))
                {
                    userMessage = "A network error occurred. Please check your connection and try again.";
                }

                await ShowErrorMessageAsync(userMessage);
                throw new InvalidOperationException(userMessage, ex);
            }
            finally
            {
                if (resourceKey == null)
                    _operationInProgress = false;

                if (lockAcquired)
                {
                    try
                    {
                        lockToUse.Release();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error releasing lock: {ex.Message}");
                    }
                }

                Debug.WriteLine($"Released operation lock for: {operationName}");
                Debug.WriteLine($"END: {operationName}");
            }
        }

        // Overload for void operations
        private async Task ExecuteOperationSafelyAsync(Func<Task> operation, string operationName, string operationType = null)
        {
            // Check for cancellation
            if (_globalCancellationTokenSource.IsCancellationRequested)
            {
                Debug.WriteLine($"Operation {operationName} cancelled");
                return;
            }

            // Create a unique key for this operation type
            string lockKey = operationType ?? operationName;

            // Get or create a semaphore for this operation type
            var semaphore = _operationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

            bool lockAcquired = false;

            try
            {
                // Try to acquire lock with timeout
                lockAcquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    Debug.WriteLine($"Timed out waiting for lock: {operationName}");
                    throw new TimeoutException($"Operation timed out: {operationName}");
                }

                StatusMessage = operationName;
                OnPropertyChanged(nameof(StatusMessage));

                await operation();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in {operationName}: {ex.Message}");

                // Don't show UI errors for cancelled tasks
                if (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    Debug.WriteLine($"{operationName} was canceled");
                    return;
                }

                await WindowManager.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Error in {operationName}: {ex.Message}",
                        "Operation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                // Always release the semaphore if we acquired it
                if (lockAcquired)
                {
                    try
                    {
                        semaphore.Release();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error releasing semaphore: {ex.Message}");
                    }
                }

                // Reset status
                StatusMessage = "Ready";
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        /// <summary>
        /// Shows loading indicator while executing an operation with proper error handling
        /// </summary>
        /// <param name="message">Message to display during loading</param>
        /// <param name="action">The async operation to execute</param>
        /// <param name="successMessage">Optional success message to show on completion</param>
        /// <returns>Task representing the operation</returns>
        private async Task ShowLoadingAsync(string loadingMessage, Func<Task> action, string successMessage = null)
        {
            try
            {
                // Set loading state
                LoadingMessage = loadingMessage;
                IsLoading = true;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingMessage));

                // Execute the operation
                await action();

                // Show success message if provided
                if (!string.IsNullOrEmpty(successMessage))
                {
                    await WindowManager.InvokeAsync(() =>
                        MessageBox.Show(
                            successMessage,
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Operation error: {ex.Message}");
                await WindowManager.InvokeAsync(() =>
                    MessageBox.Show(
                        $"An error occurred: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
            }
            finally
            {
                // Always reset loading state, even on error
                IsLoading = false;
                LoadingMessage = "Processing...";
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }

        /// <summary>
        /// Shows loading indicator while executing an operation with proper error handling and return value
        /// </summary>
        /// <typeparam name="T">The return type of the operation</typeparam>
        /// <param name="message">Message to display during loading</param>
        /// <param name="action">The async operation to execute</param>
        /// <param name="successMessage">Optional success message to show on completion</param>
        /// <returns>The result of the operation or default value if operation failed</returns>
        private async Task<T> ShowLoadingAsync<T>(string message, Func<Task<T>> action, string successMessage = null)
        {
            try
            {
                LoadingMessage = message;
                IsLoading = true;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingMessage));

                var result = await action();

                if (!string.IsNullOrEmpty(successMessage))
                {
                    await ShowSuccessMessage(successMessage);
                }

                return result;
            }
            catch (InvalidOperationException ex)
            {
                // Handle specific validation errors with the original message
                Debug.WriteLine($"Validation error in operation: {ex.Message}");
                await ShowErrorMessageAsync(ex.Message);
                return default;
            }
            catch (Exception ex)
            {
                // For all other exceptions, provide a more generic user-friendly message
                Debug.WriteLine($"Error in operation: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                string userMessage = "An unexpected error occurred. Please review your input and try again.";
                if (ex.Message.Contains("database") || ex.Message.Contains("SQL"))
                {
                    userMessage = "A database error occurred. The operation could not be completed.";
                }

                await ShowErrorMessageAsync(userMessage);
                return default;
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }

        protected override void SubscribeToEvents()
        {
            try
            {
                _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
                _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);

                base.SubscribeToEvents();

                _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(async evt =>
                {
                    try
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            if (evt.Action == "Update" && SelectedCustomer?.CustomerId == evt.Entity.CustomerId)
                            {
                                var updatedCustomer = await _customerService.GetByIdAsync(evt.Entity.CustomerId);
                                SelectedCustomer = updatedCustomer;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error handling customer update event: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error subscribing to events: {ex.Message}");
            }
        }

        protected override void UnsubscribeFromEvents()
        {
            try
            {
                _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
                _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error unsubscribing from events: {ex.Message}");
            }
        }

        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var products = await _productService.GetAllAsync();

                        // Filter to only include active products
                        var activeProducts = products.Where(p => p.IsActive).ToList();
                        AllProducts = new ObservableCollection<ProductDTO>(activeProducts);

                        var internetProducts = activeProducts
                            .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                            .ToList();
                        FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);

                        // Update UI properties
                        OnPropertyChanged(nameof(AllProducts));
                        OnPropertyChanged(nameof(FilteredProducts));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error refreshing products: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling product change: {ex.Message}");
            }
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        switch (evt.Action)
                        {
                            case "Create":
                                StartNewTransaction();
                                break;
                            case "Update":
                                // If we're editing the current transaction, refresh it
                                if (IsEditingTransaction && CurrentTransaction?.TransactionId == evt.Entity.TransactionId)
                                {
                                    IsEditingTransaction = false;
                                    StatusMessage = "Transaction updated externally - Refreshing...";
                                    OnPropertyChanged(nameof(StatusMessage));
                                    // Load the transaction again
                                    LookupTransactionId = evt.Entity.TransactionId.ToString();
                                }
                                break;
                            case "Delete":
                                // If we're editing a transaction that was deleted, reset the view
                                if (IsEditingTransaction && CurrentTransaction?.TransactionId == evt.Entity.TransactionId)
                                {
                                    StartNewTransaction();
                                    StatusMessage = "The transaction was deleted by another user";
                                    OnPropertyChanged(nameof(StatusMessage));
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in transaction change handler: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling transaction change: {ex.Message}");
            }
        }

        private async Task LoadDataAsync()
        {
            await ShowLoadingAsync("Loading products...", async () =>
            {
                Debug.WriteLine("Starting LoadDataAsync");

                var products = await _productService.GetAllAsync();
                Debug.WriteLine($"Loaded {products.Count()} products from service");

                // Filter to only include active products
                var activeProducts = products.Where(p => p.IsActive).ToList();
                Debug.WriteLine($"Filtered down to {activeProducts.Count} active products");

                // Cache images asynchronously - won't block UI
                _ = CacheProductImagesAsync(activeProducts);

                _allProducts = new ObservableCollection<ProductDTO>(activeProducts);
                FilterProductsForDropdown(string.Empty);
                Debug.WriteLine($"Initialized {_allProducts.Count} active products");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(FilteredProducts));
                    OnPropertyChanged(nameof(AllProducts));
                });
            });
        }

        private async Task LoadExchangeRate(IBusinessSettingsService businessSettingsService)
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                try
                {
                    var rateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
                    if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
                    {
                        CurrencyHelper.UpdateExchangeRate(rate);
                        Debug.WriteLine($"Exchange rate loaded: {rate}");
                    }
                    else
                    {
                        Debug.WriteLine("Exchange rate not found or invalid - using default");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading exchange rate: {ex.Message}");
                    // Don't rethrow - use default exchange rate
                }
            }, "Loading exchange rate");
        }
    }
}