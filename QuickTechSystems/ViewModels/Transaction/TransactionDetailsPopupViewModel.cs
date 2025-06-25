using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Views;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace QuickTechSystems.ViewModels.Transaction
{
    public class TransactionDetailsPopupViewModel : ViewModelBase, IDataErrorInfo
    {
        #region Private Fields
        private readonly ITransactionService _transactionService;
        private readonly Dictionary<int, TransactionDetailState> _originalStates;
        private readonly Dictionary<int, TransactionDetailState> _pendingChanges;
        private readonly Dictionary<string, string> _validationErrors;
        private readonly object _lockSync = new object();
        private readonly CancellationTokenSource _cancellationTokenSource;

        private SemaphoreSlim _operationLock;
        private volatile bool _isDisposed = false;
        private volatile bool _isInitialized = false;
        private TransactionDetailsPopup? _view;

        // Backing fields
        private ExtendedTransactionDTO _originalTransaction;
        private int _transactionId;
        private string _customerName = string.Empty;
        private DateTime _transactionDate;
        private string _paymentMethod = string.Empty;
        private string _cashierName = string.Empty;
        private decimal _totalAmount;
        private decimal _subTotal;
        private bool _isLoading;
        private bool _hasChanges;
        private ObservableCollection<TransactionDetailDTO> _transactionDetails;
        private TransactionDetailDTO? _selectedDetail;
        #endregion

        #region Events
        public event EventHandler? RequestClose;
        public event EventHandler<TransactionChangedEventArgs>? TransactionChanged;
        #endregion

        #region Properties
        public int TransactionId
        {
            get => _transactionId;
            set => SetProperty(ref _transactionId, value);
        }

        public string CustomerName
        {
            get => _customerName;
            set => SetProperty(ref _customerName, value);
        }

        public DateTime TransactionDate
        {
            get => _transactionDate;
            set => SetProperty(ref _transactionDate, value);
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            set => SetProperty(ref _paymentMethod, value);
        }

        public string CashierName
        {
            get => _cashierName;
            set => SetProperty(ref _cashierName, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            set => SetProperty(ref _totalAmount, value);
        }

        public decimal SubTotal
        {
            get => _subTotal;
            set => SetProperty(ref _subTotal, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                if (SetProperty(ref _hasChanges, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public ObservableCollection<TransactionDetailDTO> TransactionDetails
        {
            get => _transactionDetails;
            set => SetProperty(ref _transactionDetails, value);
        }

        public TransactionDetailDTO? SelectedDetail
        {
            get => _selectedDetail;
            set => SetProperty(ref _selectedDetail, value);
        }

        public bool HasValidationErrors => _validationErrors.Any();
        public string ValidationSummary => string.Join("; ", _validationErrors.Values);
        #endregion

        #region Commands
        public ICommand CloseCommand { get; }
        public ICommand EditDetailCommand { get; }
        public ICommand EditQuantityCommand { get; }
        public ICommand EditDiscountCommand { get; }
        public ICommand SaveChangesCommand { get; }
        public ICommand RemoveDetailCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CancelChangesCommand { get; }
        #endregion

        #region Constructor
        public TransactionDetailsPopupViewModel(
            ITransactionService transactionService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _transactionDetails = new ObservableCollection<TransactionDetailDTO>();
            _originalStates = new Dictionary<int, TransactionDetailState>();
            _pendingChanges = new Dictionary<int, TransactionDetailState>();
            _validationErrors = new Dictionary<string, string>();
            _operationLock = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();

            // Initialize commands
            CloseCommand = new RelayCommand(ExecuteClose);
            EditDetailCommand = new RelayCommand(EditDetail);
            EditQuantityCommand = new RelayCommand(parameter => EditSpecificField(parameter, "Quantity"));
            EditDiscountCommand = new RelayCommand(parameter => EditSpecificField(parameter, "Discount"));
            SaveChangesCommand = new RelayCommand(async _ => await SaveChangesAsync(), CanSaveChanges);
            RemoveDetailCommand = new RelayCommand(async parameter => await RemoveDetailAsync(parameter), CanRemoveDetail);
            RefreshCommand = new RelayCommand(async _ => await RefreshDataAsync(), CanRefresh);
            CancelChangesCommand = new RelayCommand(CancelChanges, CanCancelChanges);
        }
        #endregion

        #region Public Methods
        public async Task InitializeAsync(ExtendedTransactionDTO transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            ThrowIfDisposed();

            try
            {
                var operationLock = await GetValidOperationLockAsync();
                await operationLock.WaitAsync(_cancellationTokenSource.Token);
                try
                {
                    IsLoading = true;

                    _originalTransaction = transaction;
                    TransactionId = transaction.TransactionId;
                    CustomerName = transaction.CustomerName;
                    TransactionDate = transaction.TransactionDate;
                    PaymentMethod = transaction.PaymentMethod;
                    CashierName = transaction.CashierName;
                    TotalAmount = transaction.TotalAmount;

                    await LoadTransactionDetailsAsync();
                    CalculateSubTotal();
                    StoreOriginalStates();
                    AttachPropertyChangeHandlers();
                    ClearValidationErrors();

                    _isInitialized = true;
                    Debug.WriteLine($"TransactionDetailsPopupViewModel initialized for transaction {TransactionId}");
                }
                finally
                {
                    IsLoading = false;
                    operationLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Initialize operation was cancelled");
                throw;
            }
            catch (ObjectDisposedException)
            {
                throw new InvalidOperationException("Cannot initialize a disposed TransactionDetailsPopupViewModel");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing TransactionDetailsPopupViewModel: {ex}");
                await HandleExceptionAsync("Error initializing transaction details", ex);
                throw;
            }
        }

        public void SetView(TransactionDetailsPopup view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }
        #endregion

        #region Private Methods - Core Operations
        private async Task<SemaphoreSlim> GetValidOperationLockAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lockSync)
                {
                    ThrowIfDisposed();
                    return _operationLock;
                }
            });
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TransactionDetailsPopupViewModel));
        }

        private async Task LoadTransactionDetailsAsync()
        {
            try
            {
                var refreshedTransaction = await ExecuteDbOperationAsync(
                    () => _transactionService.GetTransactionWithDetailsAsync(TransactionId),
                    "Loading transaction details");

                if (refreshedTransaction != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DetachPropertyChangeHandlers();
                        TransactionDetails.Clear();

                        foreach (var detail in refreshedTransaction.Details)
                        {
                            TransactionDetails.Add(detail);
                        }

                        AttachPropertyChangeHandlers();
                        Debug.WriteLine($"Loaded {TransactionDetails.Count} transaction details");
                    });
                }
                else
                {
                    ShowTemporaryErrorMessage("Transaction not found or has been deleted.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading transaction details: {ex}");
                await HandleExceptionAsync("Error loading transaction details", ex);
            }
        }

        private void StoreOriginalStates()
        {
            _originalStates.Clear();
            foreach (var detail in TransactionDetails)
            {
                _originalStates[detail.TransactionDetailId] = new TransactionDetailState
                {
                    Quantity = detail.Quantity,
                    Discount = detail.Discount,
                    Total = detail.Total
                };
            }
            Debug.WriteLine($"Stored original states for {_originalStates.Count} details");
        }

        private void AttachPropertyChangeHandlers()
        {
            foreach (var detail in TransactionDetails)
            {
                detail.PropertyChanged -= OnDetailPropertyChanged; // Ensure no double subscription
                detail.PropertyChanged += OnDetailPropertyChanged;
            }
        }

        private void DetachPropertyChangeHandlers()
        {
            foreach (var detail in TransactionDetails)
            {
                detail.PropertyChanged -= OnDetailPropertyChanged;
            }
        }

        private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isDisposed) return;

            if (sender is TransactionDetailDTO detail &&
                (e.PropertyName == nameof(TransactionDetailDTO.Quantity) ||
                 e.PropertyName == nameof(TransactionDetailDTO.Discount)))
            {
                ValidateAndUpdateDetail(detail);
                CheckForChanges();
                CalculateSubTotal();
            }
        }

        private void ValidateAndUpdateDetail(TransactionDetailDTO detail)
        {
            var isValid = true;
            var detailKey = $"Detail_{detail.TransactionDetailId}";

            // Clear previous validation errors for this detail
            _validationErrors.Remove(detailKey);

            // Validate quantity
            if (detail.Quantity < 0)
            {
                detail.Quantity = 0;
                _validationErrors[detailKey] = "Quantity cannot be negative.";
                isValid = false;
            }

            // Validate discount
            if (detail.Discount < 0)
            {
                detail.Discount = 0;
                _validationErrors[detailKey] = "Discount cannot be negative.";
                isValid = false;
            }

            // Calculate new total
            var calculatedTotal = (detail.Quantity * detail.UnitPrice) - detail.Discount;
            if (calculatedTotal < 0)
            {
                detail.Discount = detail.Quantity * detail.UnitPrice;
                calculatedTotal = 0;
                _validationErrors[detailKey] = "Discount cannot exceed the item total.";
                isValid = false;
            }

            detail.Total = calculatedTotal;

            // Track pending changes only if valid
            if (isValid)
            {
                if (!_pendingChanges.ContainsKey(detail.TransactionDetailId))
                {
                    _pendingChanges[detail.TransactionDetailId] = new TransactionDetailState();
                }

                _pendingChanges[detail.TransactionDetailId].Quantity = detail.Quantity;
                _pendingChanges[detail.TransactionDetailId].Discount = detail.Discount;
                _pendingChanges[detail.TransactionDetailId].Total = detail.Total;
            }

            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationSummary));

            if (!isValid)
            {
                ShowTemporaryErrorMessage(_validationErrors[detailKey]);
            }
        }

        private void CheckForChanges()
        {
            var hasAnyChanges = false;

            foreach (var detail in TransactionDetails)
            {
                if (_originalStates.TryGetValue(detail.TransactionDetailId, out var originalState))
                {
                    if (Math.Abs(originalState.Quantity - detail.Quantity) > 0.001m ||
                        Math.Abs(originalState.Discount - detail.Discount) > 0.001m)
                    {
                        hasAnyChanges = true;
                        break;
                    }
                }
            }

            HasChanges = hasAnyChanges;
        }

        private void CalculateSubTotal()
        {
            SubTotal = TransactionDetails.Sum(d => d.Total);
        }

        private void ClearValidationErrors()
        {
            _validationErrors.Clear();
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(ValidationSummary));
        }

        private void RefreshCommandStates()
        {
            // Use CommandManager to refresh all command states
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        #endregion

        #region Command Implementations
        private void ExecuteClose(object? parameter)
        {
            if (HasChanges && !HasValidationErrors)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private bool CanSaveChanges(object? parameter) => HasChanges && !IsLoading && _isInitialized && !HasValidationErrors;
        private bool CanRemoveDetail(object? parameter) => parameter != null && !IsLoading && _isInitialized;
        private bool CanRefresh(object? parameter) => !IsLoading && _isInitialized;
        private bool CanCancelChanges(object? parameter) => HasChanges && !IsLoading;

        private void EditDetail(object? parameter)
        {
            if (parameter is TransactionDetailDTO detail)
            {
                SelectedDetail = detail;
                _view?.TriggerEditMode(detail, "Quantity");
            }
        }

        private void EditSpecificField(object? parameter, string fieldName)
        {
            if (parameter is TransactionDetailDTO detail)
            {
                SelectedDetail = detail;
                _view?.TriggerEditMode(detail, fieldName);
            }
        }

        private void CancelChanges(object? parameter)
        {
            if (!HasChanges) return;

            var result = MessageBox.Show(
                "Are you sure you want to cancel all changes?",
                "Cancel Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                RestoreOriginalValues();
            }
        }

        private void RestoreOriginalValues()
        {
            DetachPropertyChangeHandlers();
            try
            {
                foreach (var detail in TransactionDetails)
                {
                    if (_originalStates.TryGetValue(detail.TransactionDetailId, out var originalState))
                    {
                        detail.Quantity = originalState.Quantity;
                        detail.Discount = originalState.Discount;
                        detail.Total = originalState.Total;
                    }
                }

                _pendingChanges.Clear();
                ClearValidationErrors();
                CalculateSubTotal();
                HasChanges = false;
            }
            finally
            {
                AttachPropertyChangeHandlers();
            }
        }

        private async Task RefreshDataAsync()
        {
            if (!_isInitialized) return;

            try
            {
                var operationLock = await GetValidOperationLockAsync();
                await operationLock.WaitAsync(_cancellationTokenSource.Token);
                try
                {
                    IsLoading = true;
                    await LoadTransactionDetailsAsync();
                    CalculateSubTotal();
                    StoreOriginalStates();
                    _pendingChanges.Clear();
                    ClearValidationErrors();
                    HasChanges = false;

                    await ShowSuccessMessage("Data refreshed successfully!");
                }
                finally
                {
                    IsLoading = false;
                    operationLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Refresh operation was cancelled");
            }
            catch (ObjectDisposedException)
            {
                ShowTemporaryErrorMessage("Operation cannot be completed - dialog is closing.");
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error refreshing data", ex);
            }
        }

        private async Task RemoveDetailAsync(object? parameter)
        {
            if (parameter is not TransactionDetailDTO detail) return;

            var result = MessageBox.Show(
                $"Are you sure you want to remove '{detail.ProductName}' from this transaction?\n\n" +
                "The product will be restocked automatically.",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var operationLock = await GetValidOperationLockAsync();
                await operationLock.WaitAsync(_cancellationTokenSource.Token);
                try
                {
                    IsLoading = true;

                    var success = await ExecuteDbOperationAsync(
                        () => _transactionService.RemoveTransactionDetailAsync(detail.TransactionDetailId),
                        "Removing transaction detail");

                    if (success)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            detail.PropertyChanged -= OnDetailPropertyChanged;
                            TransactionDetails.Remove(detail);
                            _originalStates.Remove(detail.TransactionDetailId);
                            _pendingChanges.Remove(detail.TransactionDetailId);

                            // Clear validation errors for this detail
                            var detailKey = $"Detail_{detail.TransactionDetailId}";
                            _validationErrors.Remove(detailKey);
                        });

                        CalculateSubTotal();
                        TotalAmount = SubTotal;
                        CheckForChanges();

                        await ShowSuccessMessage("Item removed successfully and restocked!");
                        TransactionChanged?.Invoke(this, new TransactionChangedEventArgs(TransactionId));
                    }
                    else
                    {
                        ShowTemporaryErrorMessage("Failed to remove item.");
                    }
                }
                finally
                {
                    IsLoading = false;
                    operationLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Remove operation was cancelled");
            }
            catch (ObjectDisposedException)
            {
                ShowTemporaryErrorMessage("Operation cannot be completed - dialog is closing.");
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error removing transaction detail", ex);
            }
        }

        private async Task SaveChangesAsync()
        {
            if (!HasChanges || HasValidationErrors) return;

            try
            {
                var operationLock = await GetValidOperationLockAsync();
                await operationLock.WaitAsync(_cancellationTokenSource.Token);
                try
                {
                    IsLoading = true;
                    var changesSaved = false;
                    var errors = new List<string>();

                    foreach (var detail in TransactionDetails)
                    {
                        if (_originalStates.TryGetValue(detail.TransactionDetailId, out var originalState) &&
                            _pendingChanges.ContainsKey(detail.TransactionDetailId))
                        {
                            var pendingState = _pendingChanges[detail.TransactionDetailId];

                            // Update quantity if changed
                            if (Math.Abs(originalState.Quantity - pendingState.Quantity) > 0.001m)
                            {
                                var success = await ExecuteDbOperationAsync(
                                    () => _transactionService.UpdateTransactionDetailQuantityAsync(
                                        detail.TransactionDetailId, pendingState.Quantity),
                                    "Updating transaction detail quantity");

                                if (success)
                                {
                                    originalState.Quantity = pendingState.Quantity;
                                    originalState.Total = pendingState.Total;
                                    changesSaved = true;
                                }
                                else
                                {
                                    errors.Add($"Failed to update quantity for {detail.ProductName}");
                                }
                            }

                            // Update discount if changed
                            if (Math.Abs(originalState.Discount - pendingState.Discount) > 0.001m)
                            {
                                var success = await ExecuteDbOperationAsync(
                                    () => _transactionService.UpdateTransactionDetailDiscountAsync(
                                        detail.TransactionDetailId, pendingState.Discount),
                                    "Updating transaction detail discount");

                                if (success)
                                {
                                    originalState.Discount = pendingState.Discount;
                                    originalState.Total = pendingState.Total;
                                    changesSaved = true;
                                }
                                else
                                {
                                    errors.Add($"Failed to update discount for {detail.ProductName}");
                                }
                            }
                        }
                    }

                    if (changesSaved)
                    {
                        _pendingChanges.Clear();
                        HasChanges = false;
                        TotalAmount = SubTotal;

                        if (errors.Any())
                        {
                            var errorMessage = "Some changes were saved, but the following errors occurred:\n" +
                                               string.Join("\n", errors);
                            ShowTemporaryErrorMessage(errorMessage);
                        }
                        else
                        {
                            await ShowSuccessMessage("Changes saved successfully!");
                        }

                        TransactionChanged?.Invoke(this, new TransactionChangedEventArgs(TransactionId));
                    }
                    else
                    {
                        if (errors.Any())
                        {
                            var errorMessage = "Failed to save changes:\n" + string.Join("\n", errors);
                            ShowTemporaryErrorMessage(errorMessage);
                        }
                        else
                        {
                            ShowTemporaryErrorMessage("No changes were saved.");
                        }
                    }
                }
                finally
                {
                    IsLoading = false;
                    operationLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Save operation was cancelled");
            }
            catch (ObjectDisposedException)
            {
                ShowTemporaryErrorMessage("Operation cannot be completed - dialog is closing.");
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error saving changes", ex);
            }
        }
        #endregion

        #region IDataErrorInfo Implementation
        public string Error => ValidationSummary;

        public string this[string columnName]
        {
            get
            {
                return _validationErrors.TryGetValue(columnName, out var error) ? error : string.Empty;
            }
        }
        #endregion

        #region Disposal
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                lock (_lockSync)
                {
                    if (!_isDisposed)
                    {
                        _isDisposed = true;

                        _cancellationTokenSource?.Cancel();
                        DetachPropertyChangeHandlers();

                        _operationLock?.Dispose();
                        _cancellationTokenSource?.Dispose();

                        Debug.WriteLine($"TransactionDetailsPopupViewModel disposed for transaction {TransactionId}");
                    }
                }
            }
            base.Dispose(disposing);
        }
        #endregion

        #region Helper Classes
        private class TransactionDetailState
        {
            public decimal Quantity { get; set; }
            public decimal Discount { get; set; }
            public decimal Total { get; set; }
        }
        #endregion
    }

    public class TransactionChangedEventArgs : EventArgs
    {
        public int TransactionId { get; }

        public TransactionChangedEventArgs(int transactionId)
        {
            TransactionId = transactionId;
        }
    }
}