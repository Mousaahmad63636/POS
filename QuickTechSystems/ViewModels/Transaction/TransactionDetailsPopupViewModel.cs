using System.Collections.ObjectModel;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using QuickTechSystems.Views;

namespace QuickTechSystems.ViewModels.Transaction
{
    public class TransactionDetailsPopupViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly Dictionary<int, TransactionDetailState> _originalStates;
        private readonly Dictionary<int, TransactionDetailState> _pendingChanges;
        private readonly SemaphoreSlim _operationLock;
        private TransactionDetailsPopup? _view;

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

        public event EventHandler? RequestClose;
        public event EventHandler<TransactionChangedEventArgs>? TransactionChanged;

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
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
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

        public ICommand CloseCommand { get; }
        public ICommand SaveChangesCommand { get; }
        public ICommand EditDetailCommand { get; }
        public ICommand EditQuantityCommand { get; }
        public ICommand EditDiscountCommand { get; }
        public ICommand RemoveDetailCommand { get; }

        public TransactionDetailsPopupViewModel(
            ITransactionService transactionService,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _transactionService = transactionService;
            _transactionDetails = new ObservableCollection<TransactionDetailDTO>();
            _originalStates = new Dictionary<int, TransactionDetailState>();
            _pendingChanges = new Dictionary<int, TransactionDetailState>();
            _operationLock = new SemaphoreSlim(1, 1);

            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty));
            SaveChangesCommand = new RelayCommand(async _ => await SaveChangesAsync(), _ => HasChanges && !IsLoading);
            EditDetailCommand = new RelayCommand(EditDetail);
            EditQuantityCommand = new RelayCommand(parameter => EditSpecificField(parameter, "Quantity"));
            EditDiscountCommand = new RelayCommand(parameter => EditSpecificField(parameter, "Discount"));
            RemoveDetailCommand = new RelayCommand(async parameter => await RemoveDetailAsync(parameter));
        }

        public async Task InitializeAsync(ExtendedTransactionDTO transaction)
        {
            await _operationLock.WaitAsync();
            try
            {
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
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public void SetView(TransactionDetailsPopup view)
        {
            _view = view;
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
                        TransactionDetails.Clear();
                        foreach (var detail in refreshedTransaction.Details)
                        {
                            TransactionDetails.Add(detail);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
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
        }

        private void AttachPropertyChangeHandlers()
        {
            foreach (var detail in TransactionDetails)
            {
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
            if (sender is TransactionDetailDTO detail &&
                (e.PropertyName == nameof(TransactionDetailDTO.Quantity) ||
                 e.PropertyName == nameof(TransactionDetailDTO.Discount)))
            {
                UpdateDetailTotal(detail);
                CheckForChanges();
                CalculateSubTotal();
            }
        }

        private void UpdateDetailTotal(TransactionDetailDTO detail)
        {
            detail.Total = (detail.Quantity * detail.UnitPrice) - detail.Discount;

            if (!_pendingChanges.ContainsKey(detail.TransactionDetailId))
            {
                _pendingChanges[detail.TransactionDetailId] = new TransactionDetailState();
            }

            _pendingChanges[detail.TransactionDetailId].Quantity = detail.Quantity;
            _pendingChanges[detail.TransactionDetailId].Discount = detail.Discount;
            _pendingChanges[detail.TransactionDetailId].Total = detail.Total;
        }

        private void CheckForChanges()
        {
            var hasAnyChanges = false;

            foreach (var detail in TransactionDetails)
            {
                if (_originalStates.TryGetValue(detail.TransactionDetailId, out var originalState))
                {
                    if (originalState.Quantity != detail.Quantity ||
                        originalState.Discount != detail.Discount)
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

            await _operationLock.WaitAsync();
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
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error removing transaction detail", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task SaveChangesAsync()
        {
            if (!HasChanges) return;

            await _operationLock.WaitAsync();
            try
            {
                IsLoading = true;
                var changesSaved = false;
                var changedDetails = new List<TransactionDetailDTO>();

                foreach (var detail in TransactionDetails)
                {
                    if (_originalStates.TryGetValue(detail.TransactionDetailId, out var originalState) &&
                        _pendingChanges.ContainsKey(detail.TransactionDetailId))
                    {
                        var pendingState = _pendingChanges[detail.TransactionDetailId];

                        if (originalState.Quantity != pendingState.Quantity)
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
                                changedDetails.Add(detail);
                            }
                        }

                        if (originalState.Discount != pendingState.Discount)
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
                                changedDetails.Add(detail);
                            }
                        }
                    }
                }

                if (changesSaved)
                {
                    _pendingChanges.Clear();
                    HasChanges = false;
                    TotalAmount = SubTotal;

                    await ShowSuccessMessage("Changes saved successfully!");
                    TransactionChanged?.Invoke(this, new TransactionChangedEventArgs(TransactionId));
                }
                else
                {
                    ShowTemporaryErrorMessage("No changes were saved.");
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error saving changes", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachPropertyChangeHandlers();
                _operationLock?.Dispose();
            }
            base.Dispose(disposing);
        }

        private class TransactionDetailState
        {
            public decimal Quantity { get; set; }
            public decimal Discount { get; set; }
            public decimal Total { get; set; }
        }
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