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
    public class CustomerDebtViewModel : ViewModelBase
    {
        #region Private Fields
        private readonly ICustomerService _customerService;
        private readonly ITransactionService _transactionService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IDrawerService _drawerService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
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
                    // Just notify that the property changed
                    OnPropertyChanged(nameof(ShowPaymentMethod));
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

        public ICommand ProcessPaymentCommand { get; }
        public ICommand ViewTransactionDetailCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand AddTransactionCommand { get; }
        public ICommand SaveTransactionCommand { get; }

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

        #region Constructor
        public CustomerDebtViewModel(
            ICustomerService customerService,
            ITransactionService transactionService,
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDrawerService drawerService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

            // Initialize collections
            _customersWithDebt = new ObservableCollection<CustomerDTO>();
            _paymentHistory = new ObservableCollection<CustomerPaymentDTO>();
            _transactionHistory = new ObservableCollection<TransactionDTO>();
            _transactionTypes = new ObservableCollection<string> { "Charge", "Payment" };
            _paymentMethods = new ObservableCollection<string> { "Cash", "Bank Transfer", "Check", "Credit Card" };

            // Initialize default values
            SelectedTransactionType = "Charge";
            SelectedPaymentMethod = "Cash";

            // Initialize handlers
            _customerDebtChangedHandler = HandleCustomerDebtChanged;
            _customerChangedHandler = HandleCustomerChanged;
            _transactionChangedHandler = HandleTransactionChanged;
            _paymentChangedHandler = HandlePaymentChanged;

            // Initialize commands with proper CanExecute conditions
            ProcessPaymentCommand = new AsyncRelayCommand(
                async _ => await ProcessPaymentAsync(),
                _ => CanProcessPayment
            );
            ViewTransactionDetailCommand = new RelayCommand(ShowTransactionDetail);
            SearchCommand = new AsyncRelayCommand(async _ => await SearchCustomersAsync());
            AddTransactionCommand = new RelayCommand(_ => ShowTransactionPopup());
            SaveTransactionCommand = new AsyncRelayCommand(
                async _ => await SaveTransactionAsync(),
                _ => CanSaveTransaction
            );

            // Load initial data
            _ = LoadDataAsync();
        }
        #endregion

        #region Protected Methods
        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();

            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerPaymentDTO>>(_paymentChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerPaymentDTO>>(_paymentChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);
        }

        protected override async Task LoadDataAsync()
        {
            // Use a reasonable timeout instead of 0 to avoid silently skipping updates
            if (!await _operationLock.WaitAsync(500))
            {
                Debug.WriteLine("LoadDataAsync waiting for lock - operation in progress");
                // Wait for a reasonable amount of time instead of skipping
                if (!await _operationLock.WaitAsync(3000))
                {
                    Debug.WriteLine("LoadDataAsync timed out waiting for lock");
                    return;
                }
            }

            try
            {
                IsLoading = true;
                LoadingMessage = "Refreshing customer data...";
                ClearError();

                var customers = await ExecuteDbOperationSafelyAsync(
                    () => _customerService.GetCustomersWithDebtAsync(),
                    "Loading customers with debt");

                Debug.WriteLine($"LoadDataAsync: Loaded {customers.Count()} customers with debt");

                // Update on UI thread properly
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Update existing collection rather than creating a new one
                    if (CustomersWithDebt == null)
                    {
                        CustomersWithDebt = new ObservableCollection<CustomerDTO>();
                    }
                    else
                    {
                        // Store current selected customer ID to maintain selection
                        int? selectedId = SelectedCustomer?.CustomerId;

                        CustomersWithDebt.Clear();

                        foreach (var customer in customers)
                        {
                            CustomersWithDebt.Add(customer);
                        }

                        // Restore selection if possible
                        if (selectedId.HasValue)
                        {
                            SelectedCustomer = CustomersWithDebt.FirstOrDefault(c => c.CustomerId == selectedId);
                        }
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
                _operationLock.Release();
            }
        }
        #endregion

        #region Private Methods
        private async Task<T> ExecuteDbOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Database operation")
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                // Add a small delay to ensure any previous operation is fully complete
                await Task.Delay(200);
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

        // Overload for void operations
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

        private void ClearCollections()
        {
            if (PaymentHistory != null)
                PaymentHistory.Clear();
            if (TransactionHistory != null)
                TransactionHistory.Clear();

            OnPropertyChanged(nameof(PaymentHistory));
            OnPropertyChanged(nameof(TransactionHistory));
        }

        private async Task LoadCustomerDetailsAsync()
        {
            if (!await _operationLock.WaitAsync(500))
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
                    () => _customerService.GetPaymentHistoryAsync(customerId),
                    "Loading payment history");

                var transactions = await ExecuteDbOperationSafelyAsync(
                    () => _transactionService.GetByCustomerAsync(customerId),
                    "Loading transaction history");

                // Check if customer is still the same (hasn't changed during async operation)
                if (SelectedCustomer?.CustomerId != customerId)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PaymentHistory.Clear();
                    TransactionHistory.Clear();

                    if (payments != null)
                    {
                        foreach (var payment in payments)
                        {
                            PaymentHistory.Add(payment);
                        }
                    }

                    if (transactions != null)
                    {
                        foreach (var trans in transactions.OrderByDescending(t => t.TransactionDate))
                        {
                            TransactionHistory.Add(trans);
                        }
                    }

                    // Make sure to notify property changes
                    OnPropertyChanged(nameof(PaymentHistory));
                    OnPropertyChanged(nameof(TransactionHistory));

                    // Update LBP amount in case the customer's balance changed
                    UpdateTotalAmountLBP();
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

                    // Clear and repopulate the collection
                    CustomersWithDebt.Clear();
                    foreach (var customer in customersWithDebt)
                    {
                        CustomersWithDebt.Add(customer);
                    }

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

        private void ShowTransactionPopup()
        {
            if (SelectedCustomer == null)
            {
                ShowError("Please select a customer first");
                return;
            }

            // Reset form fields
            NewTransactionAmount = 0;
            SelectedTransactionType = "Charge";
            SelectedPaymentMethod = "Cash";
            TransactionNotes = string.Empty;

            IsTransactionPopupOpen = true;
        }

        private void CloseTransactionPopup()
        {
            IsTransactionPopupOpen = false;
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

                        // Special check for payment transactions that exceed balance
                        if (SelectedTransactionType == "Payment" && NewTransactionAmount > customer.Balance)
                        {
                            ShowError($"Payment amount cannot exceed the current balance of {customer.Balance:C2}");
                            return;
                        }

                        // For cash payments, process drawer transaction
                        await ExecuteDbOperationSafelyAsync(
                            () => _drawerService.ProcessDebtPaymentAsync(
                                NewTransactionAmount,
                                customer.Name,
                                $"Debt payment - {customer.CustomerId}"),
                            "Processing drawer payment");

                        // Record customer payment
                        var payment = new CustomerPaymentDTO
                        {
                            CustomerId = SelectedCustomer.CustomerId,
                            Amount = NewTransactionAmount,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = SelectedPaymentMethod,
                            Notes = string.IsNullOrEmpty(TransactionNotes)
                                ? $"Debt payment - Balance: {customer.Balance:C2}"
                                : TransactionNotes
                        };

                        await ExecuteDbOperationSafelyAsync(
                            () => _customerService.ProcessPaymentAsync(payment),
                            "Recording payment");

                        // Reduce customer balance
                        var previousBalance = customer.Balance;
                        customer.Balance -= NewTransactionAmount;

                        // Update customer in database
                        await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.Customers.UpdateAsync(customer),
                            "Updating customer balance");

                        await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.SaveChangesAsync(),
                            "Saving changes");

                        // Publish events to update UI
                        _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                            "Update",
                            new CustomerDTO
                            {
                                CustomerId = customer.CustomerId,
                                Name = customer.Name,
                                Balance = customer.Balance
                            }));

                        _eventAggregator.Publish(new DrawerUpdateEvent(
                            "Debt Payment",
                            NewTransactionAmount,
                            $"Debt payment from {customer.Name}"
                        ));

                        await transaction.CommitAsync();

                        // Close popup before data refresh to avoid race conditions
                        IsTransactionPopupOpen = false;

                        // Ensure we update on the UI thread
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            // Add a small delay to ensure transaction is fully committed
                            await Task.Delay(200);

                            // Refresh data
                            await LoadDataAsync();
                            await LoadCustomerDetailsAsync();

                            // Explicitly notify key property changes
                            OnPropertyChanged(nameof(TotalDebtUSD));
                            OnPropertyChanged(nameof(TotalDebtLBP));
                            OnPropertyChanged(nameof(CustomersWithDebt));

                            // If the selected customer is still the same, force refresh its properties
                            if (SelectedCustomer?.CustomerId == customer.CustomerId)
                            {
                                // Update the selected customer's balance
                                SelectedCustomer.Balance = customer.Balance;
                                OnPropertyChanged(nameof(SelectedCustomer));
                                UpdateTotalAmountLBP();
                            }

                            // Show success message
                            MessageBox.Show(
                                $"Payment of {NewTransactionAmount:C2} processed successfully for {customer.Name}.\n" +
                                $"Previous balance: {previousBalance:C2}\n" +
                                $"New balance: {customer.Balance:C2}",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        });
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

        private void ShowTransactionDetail(object? parameter)
        {
            if (parameter is TransactionDTO transaction)
            {
                try
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    var detailWindow = new TransactionDetailWindow(transaction)
                    {
                        Owner = mainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    detailWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    ShowError($"Error showing transaction details: {ex.Message}");
                }
            }
        }

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
                            // Force a refresh
                            await LoadDataAsync();

                            // If the updated customer is the currently selected one, refresh details too
                            if (SelectedCustomer?.CustomerId == evt.Entity.CustomerId)
                            {
                                // Update the selected customer with latest data
                                SelectedCustomer.Balance = evt.Entity.Balance;
                                UpdateTotalAmountLBP();
                                OnPropertyChanged(nameof(SelectedCustomer));

                                await LoadCustomerDetailsAsync();
                            }
                            break;

                        case "Create":
                            await LoadDataAsync();
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
                        await LoadCustomerDetailsAsync();

                        // Also reload all data as the balance might have changed
                        await LoadDataAsync();
                    }
                    // For new transactions with balance, always reload data
                    else if (evt.Action == "Create" && evt.Entity.Balance > 0)
                    {
                        await LoadDataAsync();
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
                    // Always reload all data as payments affect balances
                    await LoadDataAsync();

                    // If the payment is for the selected customer, also update details
                    if (evt.Entity.CustomerId == SelectedCustomer?.CustomerId)
                    {
                        await LoadCustomerDetailsAsync();
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

                    switch (evt.Action)
                    {
                        case "Update":
                            var existingCustomer = CustomersWithDebt
                                .FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);
                            if (existingCustomer != null)
                            {
                                Debug.WriteLine($"Updating balance for customer {existingCustomer.Name} from {existingCustomer.Balance} to {existingCustomer.Balance + evt.Entity.Balance}");
                                existingCustomer.Balance = evt.Entity.Balance; // Replace with actual value instead of adding

                                // Update UI
                                OnPropertyChanged(nameof(CustomersWithDebt));

                                // If this is the selected customer, update total LBP amount too
                                if (SelectedCustomer?.CustomerId == existingCustomer.CustomerId)
                                {
                                    SelectedCustomer.Balance = existingCustomer.Balance;
                                    UpdateTotalAmountLBP();
                                }

                                // Also need to update total for all customers
                                TotalDebtUSD = CustomersWithDebt.Sum(c => c.Balance);
                                _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);
                                OnPropertyChanged(nameof(TotalDebtUSD));
                                OnPropertyChanged(nameof(TotalDebtLBP));
                            }
                            else
                            {
                                Debug.WriteLine($"Customer {evt.Entity.CustomerId} not found in collection, forcing reload");
                                await LoadDataAsync();
                            }
                            break;
                        case "Create":
                            Debug.WriteLine($"New customer created, forcing reload");
                            await LoadDataAsync();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in HandleCustomerDebtChanged: {ex}");
                }
            });
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
                            ShowError($"Payment amount cannot exceed the current balance of {customer.Balance:C2}");
                            return;
                        }

                        await ExecuteDbOperationSafelyAsync(
                            () => _drawerService.ProcessDebtPaymentAsync(
                                PaymentAmount,
                                customer.Name,
                                $"Debt payment - {customer.CustomerId}"),
                            "Processing drawer payment");

                        var payment = new CustomerPaymentDTO
                        {
                            CustomerId = SelectedCustomer.CustomerId,
                            Amount = PaymentAmount,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = "Cash",
                            Notes = $"Debt payment - Balance: {customer.Balance:C2}"
                        };

                        await ExecuteDbOperationSafelyAsync(
                            () => _customerService.ProcessPaymentAsync(payment),
                            "Recording payment");

                        var previousBalance = customer.Balance;
                        customer.Balance -= PaymentAmount;

                        await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.Customers.UpdateAsync(customer),
                            "Updating customer balance");

                        await ExecuteDbOperationSafelyAsync(
                            () => _unitOfWork.SaveChangesAsync(),
                            "Saving changes");

                        _eventAggregator.Publish(new EntityChangedEvent<CustomerPaymentDTO>("Create", payment));
                        _eventAggregator.Publish(new EntityChangedEvent<CustomerDTO>(
                            "Update",
                            new CustomerDTO
                            {
                                CustomerId = customer.CustomerId,
                                Name = customer.Name,
                                Balance = customer.Balance
                            }));

                        _eventAggregator.Publish(new DrawerUpdateEvent(
                            "Debt Payment",
                            PaymentAmount,
                            $"Debt payment from {customer.Name}"
                        ));

                        await transaction.CommitAsync();

                        await LoadDataAsync();
                        await LoadCustomerDetailsAsync();
                        PaymentAmount = 0;

                        await WindowManager.InvokeAsync(() =>
                            MessageBox.Show(
                                $"Payment of {payment.Amount:C2} processed successfully.\n" +
                                $"Previous balance: {previousBalance:C2}\n" +
                                $"New balance: {customer.Balance:C2}",
                                "Success",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            )
                        );
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