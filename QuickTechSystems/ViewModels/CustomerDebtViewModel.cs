using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Windows;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.Application.Helpers;
using QuickTechSystems.Domain.Interfaces.Repositories;
using AutoMapper;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;

namespace QuickTechSystems.WPF.ViewModels
{
    public class CustomerDebtViewModel : ViewModelBase, IDisposable
    {
        #region Private Fields
        private readonly ICustomerService _customerService;
        private readonly ICustomerDebtService _customerDebtService;
        private readonly ITransactionService _transactionService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IDrawerService _drawerService;
        private readonly IActivityLogger _activityLogger;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDataLoading = false;

        private Action<EntityChangedEvent<CustomerDTO>> _customerDebtChangedHandler;
        private Action<EntityChangedEvent<CustomerDTO>> _customerChangedHandler;
        private Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;
        private Action<EntityChangedEvent<CustomerPaymentDTO>> _paymentChangedHandler;

        private ObservableCollection<CustomerDTO> _customersWithDebt;
        private CustomerDTO? _selectedCustomer;
        private decimal _paymentAmount;
        private ObservableCollection<TransactionDTO> _transactionHistory;
        private ObservableCollection<CustomerPaymentDTO> _paymentHistory;
        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _loadingMessage = "Loading...";
        private bool _isProcessing;
        private string _processingMessage = string.Empty;
        private bool _hasErrors;
        private string _errorMessage = string.Empty;
        private bool _isTransactionPopupOpen;
        private decimal _newTransactionAmount;
        private string _selectedTransactionType;
        private string _selectedPaymentMethod;
        private string _transactionNotes;
        private ObservableCollection<string> _transactionTypes;
        private ObservableCollection<string> _paymentMethods;
        private decimal _totalDebtUSD;
        private decimal _totalDebtLBP;
        private decimal _totalAmountLBP;
        #endregion

        #region Public Properties
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

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                SetProperty(ref _isProcessing, value);
                OnPropertyChanged(nameof(CanProcessPayment));
                OnPropertyChanged(nameof(CanSaveTransaction));
            }
        }

        public string ProcessingMessage
        {
            get => _processingMessage;
            set => SetProperty(ref _processingMessage, value);
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

        public ObservableCollection<CustomerDTO> CustomersWithDebt
        {
            get => _customersWithDebt;
            set => SetProperty(ref _customersWithDebt, value);
        }

        public ObservableCollection<TransactionDTO> TransactionHistory
        {
            get => _transactionHistory;
            set => SetProperty(ref _transactionHistory, value);
        }

        public CustomerDTO? SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                var oldValue = _selectedCustomer;
                if (SetProperty(ref _selectedCustomer, value))
                {
                    try
                    {
                        ClearError();
                        PaymentAmount = 0;

                        // Only load details if we have a new customer
                        if (value != null && (oldValue == null || oldValue.CustomerId != value.CustomerId))
                        {
                            _ = LoadCustomerDetailsAsync();
                        }

                        UpdateTotalAmountLBP();
                        OnPropertyChanged(nameof(CanSaveTransaction));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in SelectedCustomer setter: {ex}");
                        ShowError("Error updating customer details");
                    }
                }
            }
        }

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set
            {
                SetProperty(ref _paymentAmount, value);
                OnPropertyChanged(nameof(CanProcessPayment));
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                _ = SearchCustomersAsync();
            }
        }

        public string TotalAmountLBP
        {
            get => CurrencyHelper.FormatLBP(_totalAmountLBP);
        }

        public ObservableCollection<CustomerPaymentDTO> PaymentHistory
        {
            get => _paymentHistory;
            set => SetProperty(ref _paymentHistory, value);
        }

        public bool CanProcessPayment => !IsProcessing && SelectedCustomer != null && PaymentAmount > 0;

        public bool IsTransactionPopupOpen
        {
            get => _isTransactionPopupOpen;
            set => SetProperty(ref _isTransactionPopupOpen, value);
        }

        public decimal NewTransactionAmount
        {
            get => _newTransactionAmount;
            set
            {
                SetProperty(ref _newTransactionAmount, value);
                OnPropertyChanged(nameof(CanSaveTransaction));
            }
        }

        public string SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                if (SetProperty(ref _selectedTransactionType, value))
                {
                    OnPropertyChanged(nameof(ShowPaymentMethod));
                    OnPropertyChanged(nameof(CanSaveTransaction));
                }
            }
        }

        public string SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set => SetProperty(ref _selectedPaymentMethod, value);
        }

        public string TransactionNotes
        {
            get => _transactionNotes;
            set => SetProperty(ref _transactionNotes, value);
        }

        public ObservableCollection<string> TransactionTypes
        {
            get => _transactionTypes;
            set => SetProperty(ref _transactionTypes, value);
        }

        public ObservableCollection<string> PaymentMethods
        {
            get => _paymentMethods;
            set => SetProperty(ref _paymentMethods, value);
        }

        public bool ShowPaymentMethod => SelectedTransactionType == "Payment";

        public bool CanSaveTransaction => !IsProcessing && SelectedCustomer != null &&
                                   NewTransactionAmount > 0 && !string.IsNullOrEmpty(SelectedTransactionType);

        public decimal TotalDebtUSD
        {
            get => _totalDebtUSD;
            set => SetProperty(ref _totalDebtUSD, value);
        }

        public string TotalDebtLBP
        {
            get => CurrencyHelper.FormatLBP(_totalDebtLBP);
        }
        #endregion

        #region Commands
        public ICommand ProcessPaymentCommand { get; }
        public ICommand ViewTransactionDetailCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand AddTransactionCommand { get; }
        public ICommand SaveTransactionCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CloseTransactionPopupCommand { get; }
        #endregion

        #region Constructor
        public CustomerDebtViewModel(
            ICustomerService customerService,
            ICustomerDebtService customerDebtService,
            ITransactionService transactionService,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IActivityLogger activityLogger,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _customerDebtService = customerDebtService ?? throw new ArgumentNullException(nameof(customerDebtService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _activityLogger = activityLogger ?? throw new ArgumentNullException(nameof(activityLogger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

            // Initialize collections
            _customersWithDebt = new ObservableCollection<CustomerDTO>();
            _paymentHistory = new ObservableCollection<CustomerPaymentDTO>();
            _transactionHistory = new ObservableCollection<TransactionDTO>();
            _transactionTypes = new ObservableCollection<string> { "Payment", "Charge" };
            _paymentMethods = new ObservableCollection<string> { "Cash", "Bank Transfer", "Check", "Credit Card" };

            // Initialize default values
            SelectedTransactionType = "Payment";
            SelectedPaymentMethod = "Cash";

            // Initialize handlers
            _customerDebtChangedHandler = HandleCustomerDebtChanged;
            _customerChangedHandler = HandleCustomerChanged;
            _transactionChangedHandler = HandleTransactionChanged;
            _paymentChangedHandler = HandlePaymentChanged;

            // Initialize commands
            ProcessPaymentCommand = new AsyncRelayCommand(
                async _ => await ProcessPaymentAsync(),
                _ => CanProcessPayment
            );
            ViewTransactionDetailCommand = new AsyncRelayCommand(
                async param => await ShowTransactionDetailAsync(param),
                _ => !IsProcessing
            );
            SearchCommand = new AsyncRelayCommand(async _ => await SearchCustomersAsync(), _ => !IsProcessing);
            AddTransactionCommand = new RelayCommand(_ => ShowTransactionPopup(), _ => !IsProcessing);
            SaveTransactionCommand = new AsyncRelayCommand(
                async _ => await SaveTransactionAsync(),
                _ => CanSaveTransaction
            );
            RefreshCommand = new AsyncRelayCommand(async _ => await ForceRefreshDataAsync(), _ => !IsProcessing);

            CloseTransactionPopupCommand = new RelayCommand(_ =>
            {
                IsTransactionPopupOpen = false;
                NewTransactionAmount = 0;
                TransactionNotes = string.Empty;
            }, _ => !IsProcessing);

            // Load initial data
            _ = LoadDataAsync();
        }
        #endregion

        #region Event Subscription
        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Debug.WriteLine("CustomerDebtViewModel: Subscribing to events");

            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerPaymentDTO>>(_paymentChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            Debug.WriteLine("CustomerDebtViewModel: Unsubscribing from events");

            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerPaymentDTO>>(_paymentChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);

            base.UnsubscribeFromEvents();
        }
        #endregion

        #region Data Loading
        private async Task ForceRefreshDataAsync()
        {
            if (_isDataLoading) return;

            _isDataLoading = true;

            try
            {
                IsLoading = true;
                LoadingMessage = "Refreshing data...";
                ClearError();

                // Force reload all data
                await LoadDataAsync(forceRefresh: true);

                // If a customer is selected, reload their details too
                if (SelectedCustomer != null)
                {
                    await LoadCustomerDetailsAsync(forceRefresh: true);
                }

                ShowSuccess("Data refreshed successfully");
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error refreshing data", ex);
            }
            finally
            {
                IsLoading = false;
                _isDataLoading = false;
            }
        }

        protected override async Task LoadDataAsync()
        {
            await LoadDataAsync(false);
        }

        private async Task LoadDataAsync(bool forceRefresh = false)
        {
            // Use a reasonable timeout instead of 0
            if (!await _operationLock.WaitAsync(500) && !forceRefresh)
            {
                Debug.WriteLine("LoadDataAsync waiting for lock - operation in progress");
                if (!await _operationLock.WaitAsync(3000))
                {
                    Debug.WriteLine("LoadDataAsync timed out waiting for lock");
                    return;
                }
            }

            try
            {
                if (!forceRefresh && _isDataLoading)
                {
                    Debug.WriteLine("LoadDataAsync - Data is already loading, skipping");
                    return;
                }

                _isDataLoading = true;
                IsLoading = true;
                LoadingMessage = "Loading customer debt data...";
                ClearError();

                Debug.WriteLine("LoadDataAsync: Starting to load customers with debt");

                var customers = await ExecuteDbOperationSafelyAsync(
                    () => _customerDebtService.GetCustomersWithDebtAsync(),
                    "Loading customers with debt");

                Debug.WriteLine($"LoadDataAsync: Loaded {customers.Count()} customers with debt");

                // Update on UI thread properly
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    int? selectedId = SelectedCustomer?.CustomerId;

                    // Create a new collection to avoid collection modification issues
                    var newCollection = new ObservableCollection<CustomerDTO>(customers);
                    CustomersWithDebt = newCollection;

                    // Restore selection if possible
                    if (selectedId.HasValue)
                    {
                        SelectedCustomer = CustomersWithDebt
                            .FirstOrDefault(c => c.CustomerId == selectedId.Value);
                    }

                    // Calculate totals
                    TotalDebtUSD = customers.Sum(c => c.Balance);
                    _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);

                    Debug.WriteLine($"LoadDataAsync: Total USD Debt: {TotalDebtUSD}, Total LBP Debt: {_totalDebtLBP}");

                    // Ensure all necessary properties are notified
                    OnPropertyChanged(nameof(TotalDebtUSD));
                    OnPropertyChanged(nameof(TotalDebtLBP));
                    OnPropertyChanged(nameof(CustomersWithDebt));
                    UpdateTotalAmountLBP();
                });
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error loading customers", ex);
            }
            finally
            {
                IsLoading = false;
                _isDataLoading = false;
                _operationLock.Release();
            }
        }

        private async Task LoadCustomerDetailsAsync(bool forceRefresh = false)
        {
            if (!await _operationLock.WaitAsync(500) && !forceRefresh)
            {
                Debug.WriteLine("LoadCustomerDetailsAsync waiting for lock - operation in progress");
                if (!await _operationLock.WaitAsync(3000))
                {
                    Debug.WriteLine("LoadCustomerDetailsAsync timed out waiting for lock");
                    return;
                }
            }

            try
            {
                // Initialize collections if needed
                if (PaymentHistory == null)
                    PaymentHistory = new ObservableCollection<CustomerPaymentDTO>();
                if (TransactionHistory == null)
                    TransactionHistory = new ObservableCollection<TransactionDTO>();

                // If no customer is selected, clear collections and return
                if (SelectedCustomer == null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        PaymentHistory.Clear();
                        TransactionHistory.Clear();
                        OnPropertyChanged(nameof(PaymentHistory));
                        OnPropertyChanged(nameof(TransactionHistory));
                    });
                    return;
                }

                IsLoading = true;
                LoadingMessage = "Loading customer details...";
                ClearError();

                // Store customer ID locally to prevent null reference if customer changes
                var customerId = SelectedCustomer.CustomerId;

                // Get the data using safe execution
                var payments = await ExecuteDbOperationSafelyAsync(
                    () => _customerDebtService.GetPaymentHistoryAsync(customerId),
                    "Loading payment history");

                var transactions = await ExecuteDbOperationSafelyAsync(
                    () => _transactionService.GetByCustomerAsync(customerId),
                    "Loading transaction history");

                // Get fresh customer data to ensure balance is current
                var freshCustomerData = await ExecuteDbOperationSafelyAsync(
                    () => _customerService.GetByIdAsync(customerId),
                    "Loading fresh customer data");

                // Check if customer is still the same (hasn't changed during async operation)
                if (SelectedCustomer?.CustomerId != customerId)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Update the selected customer with fresh data
                    if (freshCustomerData != null)
                    {
                        SelectedCustomer.Balance = freshCustomerData.Balance;
                        UpdateTotalAmountLBP();
                    }

                    // Create new collections to avoid modification issues
                    PaymentHistory = new ObservableCollection<CustomerPaymentDTO>(
                        payments?.OrderByDescending(p => p.PaymentDate) ?? Enumerable.Empty<CustomerPaymentDTO>());

                    TransactionHistory = new ObservableCollection<TransactionDTO>(
                        transactions?.OrderByDescending(t => t.TransactionDate) ?? Enumerable.Empty<TransactionDTO>());

                    // Make sure to notify property changes
                    OnPropertyChanged(nameof(PaymentHistory));
                    OnPropertyChanged(nameof(TransactionHistory));
                    OnPropertyChanged(nameof(SelectedCustomer));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in LoadCustomerDetailsAsync: {ex}");
                await HandleExceptionAsync("Error loading customer details", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task SearchCustomersAsync()
        {
            // Use a proper timeout instead of 0 for the wait
            if (!await _operationLock.WaitAsync(500))
            {
                Debug.WriteLine("SearchCustomersAsync waiting for lock - operation in progress");
                if (!await _operationLock.WaitAsync(3000))
                {
                    Debug.WriteLine("SearchCustomersAsync timed out waiting for lock");
                    return;
                }
            }

            try
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    await LoadDataAsync();
                    return;
                }

                IsLoading = true;
                LoadingMessage = "Searching customers...";
                ClearError();

                var customers = await ExecuteDbOperationSafelyAsync(
                    () => _customerService.GetByNameAsync(SearchText),
                    "Searching customers");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Store current selected customer ID to maintain selection if possible
                    int? selectedId = SelectedCustomer?.CustomerId;

                    // Filter to show only customers with debt
                    var customersWithDebt = customers.Where(c => c.Balance > 0).ToList();
                    Debug.WriteLine($"SearchCustomersAsync: Found {customersWithDebt.Count} customers with debt matching '{SearchText}'");

                    // Create a new collection to avoid modification issues
                    CustomersWithDebt = new ObservableCollection<CustomerDTO>(customersWithDebt);

                    // Restore selection if possible
                    if (selectedId.HasValue)
                    {
                        SelectedCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == selectedId);
                    }

                    OnPropertyChanged(nameof(CustomersWithDebt));
                });
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error searching customers", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }
        #endregion

        #region Transaction Operations
        private void ShowTransactionPopup()
        {
            if (SelectedCustomer == null)
            {
                ShowError("Please select a customer first");
                return;
            }

            // Reset form fields
            NewTransactionAmount = 0;
            SelectedTransactionType = "Payment";
            SelectedPaymentMethod = "Cash";
            TransactionNotes = string.Empty;

            IsTransactionPopupOpen = true;
        }

        private async Task SaveTransactionAsync()
        {
            if (!await _operationLock.WaitAsync(500))
            {
                ShowError("Operation already in progress. Please wait.");
                return;
            }

            try
            {
                ClearError();

                if (SelectedCustomer == null || NewTransactionAmount <= 0)
                {
                    ShowError("Please fill in all required fields");
                    return;
                }

                IsProcessing = true;
                ProcessingMessage = "Processing transaction...";

                try
                {
                    var drawer = await ExecuteDbOperationSafelyAsync(
                        () => _drawerService.GetCurrentDrawerAsync(),
                        "Checking drawer status");

                    if (drawer == null)
                    {
                        ShowError("No active cash drawer. Please open a drawer first.");
                        return;
                    }

                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var customer = await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.Customers.GetByIdAsync(SelectedCustomer.CustomerId),
                            "Loading customer data");

                        if (customer == null)
                            throw new InvalidOperationException("Customer not found");

                        var previousBalance = customer.Balance;
                        var userId = System.Windows.Application.Current.Resources["CurrentUserId"]?.ToString() ?? "system";

                        // Handle payment vs. charge differently
                        if (SelectedTransactionType == "Payment")
                        {
                            // Special check for payments that exceed balance
                            if (NewTransactionAmount > customer.Balance)
                            {
                                ShowError($"Payment amount cannot exceed the current balance of {customer.Balance:N}");
                                return;
                            }

                            // Process drawer transaction for cash payments
                            if (SelectedPaymentMethod == "Cash")
                            {
                                await ExecuteDbOperationSafelyAsync(
                                    () => _drawerService.ProcessDebtPaymentAsync(
                                        NewTransactionAmount,
                                        customer.Name,
                                        $"Debt payment - {customer.CustomerId}"),
                                    "Processing drawer payment");
                            }

                            // Record customer payment
                            var payment = new CustomerPaymentDTO
                            {
                                CustomerId = SelectedCustomer.CustomerId,
                                Amount = NewTransactionAmount,
                                PaymentDate = DateTime.Now,
                                PaymentMethod = SelectedPaymentMethod,
                                Notes = string.IsNullOrEmpty(TransactionNotes)
                                    ? $"Debt payment - Balance: {customer.Balance:N}"
                                    : TransactionNotes
                            };

                            await ExecuteDbOperationSafelyAsync(
                                () => _customerDebtService.ProcessDebtPaymentAsync(
                                    customer.CustomerId, NewTransactionAmount, SelectedPaymentMethod),
                                "Processing debt payment");

                            // Log activity
                            await _activityLogger.LogActivityAsync(
                                userId,
                                "Debt Payment",
                                $"Processed payment of {NewTransactionAmount:N} for customer {customer.Name}");

                            _eventAggregator.Publish(new EntityChangedEvent<CustomerPaymentDTO>("Create", payment));
                        }
                        else // Charge
                        {
                            // Add to customer debt
                            await ExecuteDbOperationSafelyAsync(
                                () => _customerDebtService.AddToDebtAsync(
                                    customer.CustomerId,
                                    NewTransactionAmount,
                                    TransactionNotes),
                                "Adding to customer debt");

                            // Log activity
                            await _activityLogger.LogActivityAsync(
                                userId,
                                "Debt Charge",
                                $"Added debt charge of {NewTransactionAmount:N} to customer {customer.Name}");
                        }

                        // Get updated customer data
                        var updatedCustomer = await ExecuteDbOperationSafelyAsync(
                            () => _customerService.GetByIdAsync(customer.CustomerId),
                            "Getting updated customer data");

                        await transaction.CommitAsync();

                        // Notify UI about changes
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // Close popup
                            IsTransactionPopupOpen = false;

                            // Show success message
                            string actionType = SelectedTransactionType == "Payment" ? "Payment" : "Charge";
                            MessageBox.Show(
                                $"{actionType} of {NewTransactionAmount:N} processed successfully for {customer.Name}.\n" +
                                $"Previous balance: {previousBalance:N}\n" +
                                $"New balance: {updatedCustomer?.Balance ?? 0:N}",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        });

                        // Force refresh data after transaction
                        await ForceRefreshDataAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error processing transaction", ex);
                }
            }
            finally
            {
                IsProcessing = false;
                ProcessingMessage = string.Empty;
                _operationLock.Release();
            }
        }

        private async Task ShowTransactionDetailAsync(object? parameter)
        {
            if (parameter is TransactionDTO transaction)
            {
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        var detailWindow = new TransactionDetailWindow(transaction)
                        {
                            Owner = mainWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };
                        detailWindow.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    ShowError($"Error showing transaction details: {ex.Message}");
                }
            }
        }

        private async Task ProcessPaymentAsync()
        {
            if (!await _operationLock.WaitAsync(500))
            {
                ShowError("Payment operation already in progress. Please wait.");
                return;
            }

            try
            {
                ClearError();

                if (SelectedCustomer == null || PaymentAmount <= 0)
                {
                    ShowError("Please select a customer and enter a valid amount");
                    return;
                }

                IsProcessing = true;
                ProcessingMessage = "Processing payment...";

                try
                {
                    var drawer = await ExecuteDbOperationSafelyAsync(
                        () => _drawerService.GetCurrentDrawerAsync(),
                        "Checking drawer status");

                    if (drawer == null)
                    {
                        ShowError("No active cash drawer. Please open a drawer first.");
                        return;
                    }

                    using var transaction = await _unitOfWork.BeginTransactionAsync();
                    try
                    {
                        var customer = await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.Customers.GetByIdAsync(SelectedCustomer.CustomerId),
                            "Loading customer data");

                        if (customer == null)
                            throw new InvalidOperationException("Customer not found");

                        if (PaymentAmount > customer.Balance)
                        {
                            ShowError($"Payment amount cannot exceed the current balance of {customer.Balance:N}");
                            return;
                        }

                        var previousBalance = customer.Balance;
                        var userId = System.Windows.Application.Current.Resources["CurrentUserId"]?.ToString() ?? "system";

                        // Process payment using the debt service
                        var success = await ExecuteDbOperationSafelyAsync(
                            () => _customerDebtService.ProcessDebtPaymentAsync(
                                customer.CustomerId, PaymentAmount, "Cash"),
                            "Processing debt payment");

                        if (success)
                        {
                            // Get updated customer data
                            var updatedCustomer = await ExecuteDbOperationSafelyAsync(
                                () => _customerService.GetByIdAsync(customer.CustomerId),
                                "Getting updated customer data");

                            // Log the activity
                            await _activityLogger.LogActivityAsync(
                                userId,
                                "Debt Payment",
                                $"Processed payment of {PaymentAmount:N} for customer {customer.Name}");

                            await transaction.CommitAsync();

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show(
                                    $"Payment of {PaymentAmount:N} processed successfully.\n" +
                                    $"Previous balance: {previousBalance:N}\n" +
                                    $"New balance: {updatedCustomer?.Balance ?? 0:N}",
                                    "Success",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information
                                );

                                // Reset payment amount
                                PaymentAmount = 0;
                            });

                            // Force refresh data after successful payment
                            await ForceRefreshDataAsync();
                        }
                        else
                        {
                            await transaction.RollbackAsync();
                            ShowError("Failed to process payment. Please try again.");
                        }
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error processing payment", ex);
                }
            }
            finally
            {
                IsProcessing = false;
                ProcessingMessage = string.Empty;
                _operationLock.Release();
            }
        }
        #endregion

        #region Event Handlers
        private async void HandleCustomerChanged(EntityChangedEvent<CustomerDTO> evt)
        {
            Debug.WriteLine($"Customer changed event received: {evt.Action}, CustomerId: {evt.Entity?.CustomerId}");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    switch (evt.Action)
                    {
                        case "Update":
                            // First check if we need to update our local customer list
                            var existingCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);

                            if (existingCustomer != null)
                            {
                                // Update the balance and other properties
                                existingCustomer.Balance = evt.Entity.Balance;
                                existingCustomer.Name = evt.Entity.Name;
                                existingCustomer.Phone = evt.Entity.Phone;
                                existingCustomer.Email = evt.Entity.Email;
                                existingCustomer.IsActive = evt.Entity.IsActive;
                                existingCustomer.UpdatedAt = evt.Entity.UpdatedAt;

                                // Update selected customer if it's the same one
                                if (SelectedCustomer?.CustomerId == evt.Entity.CustomerId)
                                {
                                    SelectedCustomer.Balance = evt.Entity.Balance;
                                    SelectedCustomer.Name = evt.Entity.Name;
                                    SelectedCustomer.Phone = evt.Entity.Phone;
                                    SelectedCustomer.Email = evt.Entity.Email;
                                    SelectedCustomer.IsActive = evt.Entity.IsActive;
                                    SelectedCustomer.UpdatedAt = evt.Entity.UpdatedAt;

                                    // Update LBP conversion
                                    UpdateTotalAmountLBP();
                                    OnPropertyChanged(nameof(SelectedCustomer));
                                }

                                // If balance is now 0, and we're not searching, remove them from the list
                                if (existingCustomer.Balance <= 0 && string.IsNullOrWhiteSpace(SearchText))
                                {
                                    CustomersWithDebt.Remove(existingCustomer);

                                    // If this was the selected customer, clear the selection
                                    if (SelectedCustomer?.CustomerId == existingCustomer.CustomerId)
                                    {
                                        SelectedCustomer = null;
                                    }
                                }

                                // Recalculate total debt
                                TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                                _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                                OnPropertyChanged(nameof(TotalDebtUSD));
                                OnPropertyChanged(nameof(TotalDebtLBP));
                                OnPropertyChanged(nameof(CustomersWithDebt));
                            }
                            else if (evt.Entity.Balance > 0)
                            {
                                // This is a customer that now has debt but wasn't in our list
                                // We need to add them (if not searching)
                                if (string.IsNullOrWhiteSpace(SearchText))
                                {
                                    CustomersWithDebt.Add(evt.Entity);

                                    // Recalculate total debt
                                    TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                                    _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                                    OnPropertyChanged(nameof(TotalDebtUSD));
                                    OnPropertyChanged(nameof(TotalDebtLBP));
                                    OnPropertyChanged(nameof(CustomersWithDebt));
                                }
                            }

                            // If this is the selected customer, reload their details
                            if (SelectedCustomer?.CustomerId == evt.Entity.CustomerId)
                            {
                                await LoadCustomerDetailsAsync();
                            }
                            break;

                        case "Create":
                            // If the new customer has debt, add them to our list
                            if (evt.Entity.Balance > 0)
                            {
                                if (string.IsNullOrWhiteSpace(SearchText) ||
                                    evt.Entity.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                {
                                    CustomersWithDebt.Add(evt.Entity);

                                    // Recalculate total debt
                                    TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                                    _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                                    OnPropertyChanged(nameof(TotalDebtUSD));
                                    OnPropertyChanged(nameof(TotalDebtLBP));
                                    OnPropertyChanged(nameof(CustomersWithDebt));
                                }
                            }
                            break;

                        case "Delete":
                            // Remove the customer from our list if they're in it
                            var customerToRemove = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);
                            if (customerToRemove != null)
                            {
                                CustomersWithDebt.Remove(customerToRemove);

                                // If this was the selected customer, clear the selection
                                if (SelectedCustomer?.CustomerId == customerToRemove.CustomerId)
                                {
                                    SelectedCustomer = null;
                                }

                                // Recalculate total debt
                                TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                                _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                                OnPropertyChanged(nameof(TotalDebtUSD));
                                OnPropertyChanged(nameof(TotalDebtLBP));
                                OnPropertyChanged(nameof(CustomersWithDebt));
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling customer changed event: {ex}");
                }
            });
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            Debug.WriteLine($"Transaction changed event received: {evt.Action}, CustomerId: {evt.Entity?.CustomerId}");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // If the transaction is for the selected customer, update customer details
                    if (evt.Entity.CustomerId == SelectedCustomer?.CustomerId)
                    {
                        // Add the transaction to the transaction history if it's new
                        if (evt.Action == "Create")
                        {
                            // Add at the beginning for correct chronological order
                            TransactionHistory.Insert(0, evt.Entity);
                            OnPropertyChanged(nameof(TransactionHistory));
                        }

                        // Get fresh customer data to ensure balance is up to date
                        var freshCustomerData = await ExecuteDbOperationSafelyAsync(
                            () => _customerService.GetByIdAsync(SelectedCustomer.CustomerId),
                            "Loading fresh customer data after transaction change");

                        if (freshCustomerData != null)
                        {
                            SelectedCustomer.Balance = freshCustomerData.Balance;
                            UpdateTotalAmountLBP();
                            OnPropertyChanged(nameof(SelectedCustomer));

                            // Update the customer in the list too
                            var listCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == freshCustomerData.CustomerId);
                            if (listCustomer != null)
                            {
                                listCustomer.Balance = freshCustomerData.Balance;

                                // If balance is now 0 and we're not searching, remove them
                                if (listCustomer.Balance <= 0 && string.IsNullOrWhiteSpace(SearchText))
                                {
                                    CustomersWithDebt.Remove(listCustomer);
                                }
                            }

                            // Recalculate total debt
                            TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                            _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                            OnPropertyChanged(nameof(TotalDebtUSD));
                            OnPropertyChanged(nameof(TotalDebtLBP));
                            OnPropertyChanged(nameof(CustomersWithDebt));
                        }
                    }
                    // For new transactions with non-zero balance, update customer list
                    else if (evt.Action == "Create" && evt.Entity.Balance > 0)
                    {
                        // Get the customer data
                        var customer = await ExecuteDbOperationSafelyAsync(
                            () => _customerService.GetByIdAsync(evt.Entity.CustomerId),
                            "Loading customer data after transaction");

                        if (customer != null && customer.Balance > 0)
                        {
                            // Check if customer is already in the list
                            var existingCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == customer.CustomerId);

                            if (existingCustomer != null)
                            {
                                // Update existing customer
                                existingCustomer.Balance = customer.Balance;
                            }
                            else if (string.IsNullOrWhiteSpace(SearchText) ||
                                     customer.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                            {
                                // Add the customer to the list
                                CustomersWithDebt.Add(customer);
                            }

                            // Recalculate total debt
                            TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                            _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                            OnPropertyChanged(nameof(TotalDebtUSD));
                            OnPropertyChanged(nameof(TotalDebtLBP));
                            OnPropertyChanged(nameof(CustomersWithDebt));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling transaction changed event: {ex}");
                }
            });
        }

        private async void HandlePaymentChanged(EntityChangedEvent<CustomerPaymentDTO> evt)
        {
            Debug.WriteLine($"Payment changed event received: {evt.Action}, CustomerId: {evt.Entity?.CustomerId}");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Get fresh customer data
                    var customer = await ExecuteDbOperationSafelyAsync(
                        () => _customerService.GetByIdAsync(evt.Entity.CustomerId),
                        "Loading customer data after payment change");

                    if (customer != null)
                    {
                        // Update the customer in our list if present
                        var listCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == customer.CustomerId);

                        if (listCustomer != null)
                        {
                            // Update the balance
                            listCustomer.Balance = customer.Balance;

                            // If balance is now 0 and we're not searching, remove them
                            if (listCustomer.Balance <= 0 && string.IsNullOrWhiteSpace(SearchText))
                            {
                                CustomersWithDebt.Remove(listCustomer);

                                // If this was the selected customer, clear selection
                                if (SelectedCustomer?.CustomerId == listCustomer.CustomerId)
                                {
                                    SelectedCustomer = null;
                                }
                            }
                        }

                        // If this is the selected customer, update details
                        if (SelectedCustomer?.CustomerId == customer.CustomerId)
                        {
                            // Update balance
                            SelectedCustomer.Balance = customer.Balance;
                            UpdateTotalAmountLBP();
                            OnPropertyChanged(nameof(SelectedCustomer));

                            // Add the payment to payment history if it's new
                            if (evt.Action == "Create")
                            {
                                // Add at the beginning for correct chronological order
                                PaymentHistory.Insert(0, evt.Entity);
                                OnPropertyChanged(nameof(PaymentHistory));
                            }
                        }

                        // Recalculate total debt
                        TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                        _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                        OnPropertyChanged(nameof(TotalDebtUSD));
                        OnPropertyChanged(nameof(TotalDebtLBP));
                        OnPropertyChanged(nameof(CustomersWithDebt));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling payment changed event: {ex}");
                }
            });
        }

        private async void HandleCustomerDebtChanged(EntityChangedEvent<CustomerDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    Debug.WriteLine($"Customer debt changed event received: {evt.Action}, CustomerId: {evt.Entity?.CustomerId}");

                    // Get fresh customer data to ensure accuracy
                    var customer = await ExecuteDbOperationSafelyAsync(
                        () => _customerService.GetByIdAsync(evt.Entity.CustomerId),
                        "Loading customer data for debt change");

                    if (customer != null)
                    {
                        var existingCustomer = CustomersWithDebt
                            .FirstOrDefault(c => c.CustomerId == customer.CustomerId);

                        if (existingCustomer != null)
                        {
                            Debug.WriteLine($"Updating balance for customer {existingCustomer.Name} from {existingCustomer.Balance} to {customer.Balance}");

                            // Update with new data
                            existingCustomer.Balance = customer.Balance;
                            existingCustomer.Name = customer.Name;
                            existingCustomer.Phone = customer.Phone;
                            existingCustomer.Email = customer.Email;
                            existingCustomer.IsActive = customer.IsActive;
                            existingCustomer.UpdatedAt = customer.UpdatedAt;

                            // If balance is now 0 and we're not searching, remove them
                            if (existingCustomer.Balance <= 0 && string.IsNullOrWhiteSpace(SearchText))
                            {
                                CustomersWithDebt.Remove(existingCustomer);

                                // If this was the selected customer, clear selection
                                if (SelectedCustomer?.CustomerId == existingCustomer.CustomerId)
                                {
                                    SelectedCustomer = null;
                                }
                            }
                        }
                        else if (customer.Balance > 0)
                        {
                            // This is a customer with debt who wasn't in our list
                            // Add them if not searching or if they match search
                            if (string.IsNullOrWhiteSpace(SearchText) ||
                                customer.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                            {
                                CustomersWithDebt.Add(customer);
                            }
                        }

                        // If this is the selected customer, update its details
                        if (SelectedCustomer?.CustomerId == customer.CustomerId)
                        {
                            SelectedCustomer.Balance = customer.Balance;
                            SelectedCustomer.Name = customer.Name;
                            SelectedCustomer.Phone = customer.Phone;
                            SelectedCustomer.Email = customer.Email;
                            SelectedCustomer.IsActive = customer.IsActive;
                            SelectedCustomer.UpdatedAt = customer.UpdatedAt;

                            UpdateTotalAmountLBP();
                            OnPropertyChanged(nameof(SelectedCustomer));

                            // Reload customer details
                            await LoadCustomerDetailsAsync();
                        }

                        // Recalculate total debt
                        TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                        _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                        OnPropertyChanged(nameof(TotalDebtUSD));
                        OnPropertyChanged(nameof(TotalDebtLBP));
                        OnPropertyChanged(nameof(CustomersWithDebt));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in HandleCustomerDebtChanged: {ex}");
                }
            });
        }
        #endregion

        #region Helper Methods
        private async Task<T> ExecuteDbOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Database operation")
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in {operationName}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await HandleExceptionAsync(operationName, ex);
                throw;
            }
        }

        private async Task ExecuteDbOperationSafelyAsync(Func<Task> operation, string operationName = "Database operation")
        {
            await ExecuteDbOperationSafelyAsync<bool>(async () =>
            {
                await operation();
                return true;
            }, operationName);
        }

        private async Task HandleExceptionAsync(string context, Exception ex)
        {
            Debug.WriteLine($"{context}: {ex}");

            // Special handling for known database errors
            if (ex.Message.Contains("A second operation was started") ||
                (ex.InnerException != null && ex.InnerException.Message.Contains("A second operation was started")))
            {
                ShowError("Database is busy processing another request. Please try again in a moment.");
            }
            else if (ex.Message.Contains("entity with the specified primary key") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("entity with the specified primary key")))
            {
                ShowError("Requested record not found. It may have been deleted.");
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ShowError("Database connection lost. Please check your connection and try again.");
            }
            else
            {
                ShowError($"{context}: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            HasErrors = true;
            ErrorMessage = message;

            // Log the error
            Debug.WriteLine($"ERROR: {message}");

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message) // Only clear if still the same message
                    {
                        HasErrors = false;
                        ErrorMessage = string.Empty;
                    }
                });
            });
        }

        private void ShowSuccess(string message)
        {
            Debug.WriteLine($"SUCCESS: {message}");

            Task.Run(async () =>
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        message,
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                });
            });
        }

        private void ClearError()
        {
            HasErrors = false;
            ErrorMessage = string.Empty;
        }

        private void UpdateTotalAmountLBP()
        {
            try
            {
                // Make sure we're getting a valid decimal from the selected customer
                _totalAmountLBP = SelectedCustomer?.Balance ?? 0;
                // Using invariant culture for consistent decimal handling
                _totalAmountLBP = CurrencyHelper.ConvertToLBP(_totalAmountLBP);
                OnPropertyChanged(nameof(TotalAmountLBP));

                // Debug message to verify values
                Debug.WriteLine($"UpdateTotalAmountLBP: Customer Balance: {SelectedCustomer?.Balance ?? 0}, Converted LBP: {_totalAmountLBP}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating LBP amount: {ex}");
                _totalAmountLBP = 0;
                OnPropertyChanged(nameof(TotalAmountLBP));
            }
        }
        #endregion

        #region IDisposable Implementation
        public override void Dispose()
        {
            _operationLock?.Dispose();
            UnsubscribeFromEvents();
            base.Dispose();
        }
        #endregion
    }
}