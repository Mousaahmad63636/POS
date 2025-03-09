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
            _quoteService = quoteService;
            _unitOfWork = unitOfWork;
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _drawerService = drawerService;
            _categoryService = categoryService;
            _systemPreferencesService = systemPreferencesService;
            _transactionChangedHandler = HandleTransactionChanged;
            _productChangedHandler = HandleProductChanged;

            // Subscribe to event aggregator events
            _eventAggregator.Subscribe<ApplicationModeChangedEvent>(OnApplicationModeChanged);

            InitializeCommands();
            InitializeCollections();
            StartNewTransaction();

            // Initialize data
            LoadDataAsync().ConfigureAwait(false);
            LoadExchangeRate(businessSettingsService).ConfigureAwait(false);
            LoadRestaurantModePreference().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.WriteLine($"Error initializing restaurant mode: {t.Exception}");
                }
            }, TaskScheduler.Current);

            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync());
            ProcessBarcodeCommand = new AsyncRelayCommand(async _ => await ProcessBarcodeInput());

            // Start timer to update current date/time
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => CurrentDate = DateTime.Now;
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from all events to prevent memory leaks
                if (_eventAggregator != null)
                {
                    _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
                    _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
                    _eventAggregator.Unsubscribe<ApplicationModeChangedEvent>(OnApplicationModeChanged);
                }

                // Dispose of cancellation tokens
                _customerSearchCts?.Cancel();
                _customerSearchCts?.Dispose();

                // Dispose of semaphores
                _transactionOperationLock?.Dispose();
                _categoryLoadSemaphore?.Dispose();
                _customerSearchSemaphore?.Dispose();

                // Dispose resource locks
                foreach (var lockItem in _resourceLocks.Values)
                {
                    lockItem?.Dispose();
                }
                _resourceLocks.Clear();

                // Clear dictionaries
                _imageCache?.Clear();
                _customerSpecificPrices?.Clear();
                _validationErrors?.Clear();

                // Clear collections to help with garbage collection
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

                // Cancel any pending operations
                IsLoading = false;

                // Log disposal for debugging
                Debug.WriteLine("TransactionViewModel disposed");
            }

            base.Dispose(disposing);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TransactionViewModel()
        {
            Dispose(false);
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
            await lockToUse.WaitAsync();

            if (resourceKey == null)
                _operationInProgress = true;

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                // Add a small delay to ensure any previous operation is fully complete
                await Task.Delay(50);
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in {operationName}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Error during {operationName}: {ex.Message}");
                throw;
            }
            finally
            {
                if (resourceKey == null)
                    _operationInProgress = false;
                lockToUse.Release();
                Debug.WriteLine($"Released operation lock for: {operationName}");
                Debug.WriteLine($"END: {operationName}");
            }
        }

        // Overload for void operations
        private async Task ExecuteOperationSafelyAsync(Func<Task> operation, string operationName = "Transaction operation", string resourceKey = null)
        {
            await ExecuteOperationSafelyAsync<bool>(async () =>
            {
                await operation();
                return true;
            }, operationName, resourceKey);
        }

        /// <summary>
        /// Shows loading indicator while executing an operation with proper error handling
        /// </summary>
        /// <param name="message">Message to display during loading</param>
        /// <param name="action">The async operation to execute</param>
        /// <param name="successMessage">Optional success message to show on completion</param>
        /// <returns>Task representing the operation</returns>
        private async Task ShowLoadingAsync(string message, Func<Task> action, string successMessage = null)
        {
            try
            {
                LoadingMessage = message;
                IsLoading = true;

                await action();

                if (!string.IsNullOrEmpty(successMessage))
                {
                    await ShowSuccessMessage(successMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in operation: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Operation failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
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

                var result = await action();

                if (!string.IsNullOrEmpty(successMessage))
                {
                    await ShowSuccessMessage(successMessage);
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in operation: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Operation failed: {ex.Message}");
                return default;
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);

            base.SubscribeToEvents();

            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(async evt =>
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (evt.Action == "Update" && SelectedCustomer?.CustomerId == evt.Entity.CustomerId)
                    {
                        var updatedCustomer = await _customerService.GetByIdAsync(evt.Entity.CustomerId);
                        SelectedCustomer = updatedCustomer;
                    }
                });
            });
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
        }

        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var products = await _productService.GetAllAsync();

                    // Filter to only include active products
                    var activeProducts = products.Where(p => p.IsActive).ToList();
                    AllProducts = new ObservableCollection<ProductDTO>(activeProducts);

                    var internetProducts = activeProducts
                        .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                    FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling product change: {ex.Message}");
            }
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (evt.Action)
                {
                    case "Create":
                        StartNewTransaction();
                        break;
                    case "Update":
                        break;
                    case "Delete":
                        break;
                }
            });
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

                foreach (var product in activeProducts)
                {
                    try
                    {
                        if (product.Image != null)
                        {
                            Debug.WriteLine($"Caching image for product {product.ProductId}");
                            CacheProductImage(product);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error caching image for product {product.ProductId}: {ex.Message}");
                        // Continue despite image caching errors
                    }
                }

                _allProducts = new ObservableCollection<ProductDTO>(activeProducts);
                FilterProductsForDropdown(string.Empty);
                Debug.WriteLine($"Initialized {_allProducts.Count} active products");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(FilteredProducts));
                });
            });
        }

        private async Task LoadExchangeRate(IBusinessSettingsService businessSettingsService)
        {
            await ExecuteOperationSafelyAsync(async () =>
            {
                var rateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
                if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
                {
                    CurrencyHelper.UpdateExchangeRate(rate);
                }
            }, "Loading exchange rate");
        }
    }
}