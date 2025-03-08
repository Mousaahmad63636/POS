﻿using System;

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



        private ObservableCollection<CustomerDTO> _customersWithDebt;

        private CustomerDTO? _selectedCustomer;

        private decimal _paymentAmount;

        private ObservableCollection<TransactionDTO> _transactionHistory;

        private ObservableCollection<CustomerPaymentDTO> _paymentHistory;

        private string _searchText = string.Empty;

        private decimal _totalAmountLBP;

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

            private set => SetProperty(ref _totalAmountLBP, decimal.Parse(value));

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

        #endregion



        private decimal _totalDebtUSD;

        public decimal TotalDebtUSD

        {

            get => _totalDebtUSD;

            set => SetProperty(ref _totalDebtUSD, value);

        }



        private decimal _totalDebtLBP;

        public string TotalDebtLBP

        {

            get => CurrencyHelper.FormatLBP(_totalDebtLBP);

            private set => SetProperty(ref _totalDebtLBP, decimal.Parse(value));

        }



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



            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(async evt =>

            {

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>

                {

                    switch (evt.Action)

                    {

                        case "Update":

                            await LoadDataAsync();

                            if (SelectedCustomer?.CustomerId == evt.Entity.CustomerId)

                            {

                                await LoadCustomerDetailsAsync();

                            }

                            break;

                    }

                });

            });



            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(async evt =>

            {

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>

                {

                    if (evt.Entity.CustomerId == SelectedCustomer?.CustomerId)

                    {

                        await LoadCustomerDetailsAsync();

                        await LoadDataAsync();

                    }

                    else if (evt.Action == "Create" && evt.Entity.Balance > 0)

                    {

                        await LoadDataAsync();

                    }

                });

            });



            _eventAggregator.Subscribe<EntityChangedEvent<CustomerPaymentDTO>>(async evt =>

            {

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>

                {

                    if (evt.Entity.CustomerId == SelectedCustomer?.CustomerId)

                    {

                        await LoadCustomerDetailsAsync();

                    }

                    await LoadDataAsync();

                });

            });

        }



        protected override void UnsubscribeFromEvents()

        {

            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);

        }



        protected override async Task LoadDataAsync()

        {

            if (!await _operationLock.WaitAsync(0))

            {

                Debug.WriteLine("LoadDataAsync skipped - operation in progress");

                return;

            }



            try

            {

                IsLoading = true;

                LoadingMessage = "Refreshing customer data...";



                var customers = await ExecuteDbOperationSafelyAsync(

                    () => _customerService.GetCustomersWithDebtAsync(),

                    "Loading customers with debt");



                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>

                {

                    CustomersWithDebt = new ObservableCollection<CustomerDTO>(customers);



                    // Calculate totals

                    TotalDebtUSD = customers.Sum(c => c.Balance);

                    _totalDebtLBP = CurrencyHelper.ConvertToLBP(TotalDebtUSD);

                    OnPropertyChanged(nameof(TotalDebtLBP));



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

                _totalAmountLBP = SelectedCustomer?.Balance ?? 0;

                _totalAmountLBP = CurrencyHelper.ConvertToLBP(_totalAmountLBP);

                OnPropertyChanged(nameof(TotalAmountLBP));

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

            if (!await _operationLock.WaitAsync(0))

            {

                Debug.WriteLine("LoadCustomerDetailsAsync skipped - operation in progress");

                return;

            }



            try

            {

                // Initialize empty collections if they haven't been initialized yet

                if (PaymentHistory == null)

                    PaymentHistory = new ObservableCollection<CustomerPaymentDTO>();

                if (TransactionHistory == null)

                    TransactionHistory = new ObservableCollection<TransactionDTO>();



                // Clear existing data

                PaymentHistory.Clear();

                TransactionHistory.Clear();



                // If no customer is selected, just return with empty collections

                if (SelectedCustomer == null)

                {

                    OnPropertyChanged(nameof(PaymentHistory));

                    OnPropertyChanged(nameof(TransactionHistory));

                    return;

                }



                IsLoading = true;

                LoadingMessage = "Loading customer details...";



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



                    OnPropertyChanged(nameof(PaymentHistory));

                    OnPropertyChanged(nameof(TransactionHistory));

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

            if (!await _operationLock.WaitAsync(0))

            {

                Debug.WriteLine("SearchCustomersAsync skipped - operation in progress");

                return;

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



                var customers = await ExecuteDbOperationSafelyAsync(

                    () => _customerService.GetByNameAsync(SearchText),

                    "Searching customers");



                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>

                {

                    CustomersWithDebt = new ObservableCollection<CustomerDTO>(

                        customers.Where(c => c.Balance > 0));

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



        // In CustomerDebtViewModel.cs - Modify the SaveTransactionAsync method
        private async Task SaveTransactionAsync()
        {
            if (!await _operationLock.WaitAsync(0))
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

                        if (NewTransactionAmount > customer.Balance)
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
                            PaymentMethod = "Cash", // Hardcoded to Cash
                            Notes = $"Debt payment - Balance: {customer.Balance:C2}"
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

                        // Publish events
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



        private async void HandleCustomerDebtChanged(EntityChangedEvent<CustomerDTO> evt)

        {

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>

            {

                switch (evt.Action)

                {

                    case "Update":

                        var existingCustomer = CustomersWithDebt

                            .FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);

                        if (existingCustomer != null)

                        {

                            existingCustomer.Balance += evt.Entity.Balance;

                        }

                        else

                        {

                            await LoadDataAsync();

                        }

                        break;

                    case "Create":

                        await LoadDataAsync();

                        break;

                }

            });

        }



        private async Task ProcessPaymentAsync()

        {

            if (!await _operationLock.WaitAsync(0))

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