using QuickTechSystems.Application.Events;
using QuickTechSystems.Domain.Interfaces.Repositories;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.WPF.ViewModels
{
    public class SupplierViewModel : ViewModelBase
    {
        private readonly ISupplierService _supplierService;
        private readonly IDrawerService _drawerService;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<SupplierTransactionDTO> _supplierTransactions;
        private SupplierDTO? _selectedSupplier;
        private bool _isEditing;
        private string _searchText = string.Empty;
        private decimal _paymentAmount;
        private string _notes = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isSaving;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private CancellationTokenSource _cts;
        private Action<EntityChangedEvent<SupplierDTO>> _supplierChangedHandler;
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;
        private readonly IUnitOfWork _unitOfWork;

        // Popup state properties
        private bool _isSupplierPopupOpen;
        private bool _isTransactionPopupOpen;
        private bool _isTransactionsHistoryPopupOpen;
        private bool _isNewSupplier;

        public FlowDirection CurrentFlowDirection
        {
            get => _currentFlowDirection;
            set => SetProperty(ref _currentFlowDirection, value);
        }

        // Popup state properties
        public bool IsSupplierPopupOpen
        {
            get => _isSupplierPopupOpen;
            set => SetProperty(ref _isSupplierPopupOpen, value);
        }

        public bool IsTransactionPopupOpen
        {
            get => _isTransactionPopupOpen;
            set => SetProperty(ref _isTransactionPopupOpen, value);
        }

        public bool IsTransactionsHistoryPopupOpen
        {
            get => _isTransactionsHistoryPopupOpen;
            set => SetProperty(ref _isTransactionsHistoryPopupOpen, value);
        }

        public bool IsNewSupplier
        {
            get => _isNewSupplier;
            set => SetProperty(ref _isNewSupplier, value);
        }

        // Update this when language changes
        public void UpdateFlowDirection(string language)
        {
            CurrentFlowDirection = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        public SupplierViewModel(
            ISupplierService supplierService,
            IDrawerService drawerService,
            IUnitOfWork unitOfWork,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _supplierService = supplierService;
            _drawerService = drawerService;
            _unitOfWork = unitOfWork;
            _suppliers = new ObservableCollection<SupplierDTO>();
            _supplierTransactions = new ObservableCollection<SupplierTransactionDTO>();
            _supplierChangedHandler = HandleSupplierChanged;
            _cts = new CancellationTokenSource();

            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AddCommand = new RelayCommand(_ => AddNew());
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
            AddPaymentCommand = new AsyncRelayCommand(async _ => await AddPaymentAsync());
            ShowTransactionsHistoryCommand = new RelayCommand(_ => ShowTransactionsHistoryPopup());

            _ = LoadDataAsync();
        }

        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
        }

        public ObservableCollection<SupplierTransactionDTO> SupplierTransactions
        {
            get => _supplierTransactions;
            set => SetProperty(ref _supplierTransactions, value);
        }

        public SupplierDTO? SelectedSupplier
        {
            get => _selectedSupplier;
            set
            {
                if (SetProperty(ref _selectedSupplier, value))
                {
                    IsEditing = value != null;
                    _ = LoadSupplierTransactionsAsync();
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                _ = SearchSuppliersAsync();
            }
        }

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set => SetProperty(ref _paymentAmount, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public ICommand LoadCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AddPaymentCommand { get; }
        public ICommand ShowTransactionsHistoryCommand { get; }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<SupplierDTO>>(_supplierChangedHandler);
        }

        private async void HandleSupplierChanged(EntityChangedEvent<SupplierDTO> evt)
        {
            try
            {
                Debug.WriteLine($"ProductViewModel: Handling supplier change: {evt.Action}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            // Only add if the supplier is active
                            if (evt.Entity.IsActive && !Suppliers.Any(s => s.SupplierId == evt.Entity.SupplierId))
                            {
                                Suppliers.Add(evt.Entity);
                                Debug.WriteLine($"Added new supplier {evt.Entity.Name}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Suppliers.ToList().FindIndex(s => s.SupplierId == evt.Entity.SupplierId);
                            if (existingIndex != -1)
                            {
                                if (evt.Entity.IsActive)
                                {
                                    // Update the existing supplier if it's active
                                    Suppliers[existingIndex] = evt.Entity;
                                    Debug.WriteLine($"Updated supplier {evt.Entity.Name}");
                                }
                                else
                                {
                                    // Remove the supplier if it's now inactive
                                    Suppliers.RemoveAt(existingIndex);
                                    Debug.WriteLine($"Removed inactive supplier {evt.Entity.Name}");
                                }
                            }
                            else if (evt.Entity.IsActive)
                            {
                                // This is a supplier that wasn't in our list but is now active
                                Suppliers.Add(evt.Entity);
                                Debug.WriteLine($"Added newly active supplier {evt.Entity.Name}");
                            }
                            break;
                        case "Delete":
                            var supplierToRemove = Suppliers.FirstOrDefault(s => s.SupplierId == evt.Entity.SupplierId);
                            if (supplierToRemove != null)
                            {
                                Suppliers.Remove(supplierToRemove);
                                Debug.WriteLine($"Removed supplier {supplierToRemove.Name}");
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProductViewModel: Error handling supplier change: {ex.Message}");
            }
        }

        #region Popup Management
        public void ShowSupplierPopup()
        {
            IsSupplierPopupOpen = true;
        }

        public void CloseSupplierPopup()
        {
            IsSupplierPopupOpen = false;
        }

        public void ShowTransactionPopup()
        {
            if (SelectedSupplier == null) return;

            // Reset transaction values
            PaymentAmount = 0;
            Notes = string.Empty;

            IsTransactionPopupOpen = true;
        }

        public void CloseTransactionPopup()
        {
            IsTransactionPopupOpen = false;
        }

        public void ShowTransactionsHistoryPopup()
        {
            if (SelectedSupplier == null) return;

            _ = LoadSupplierTransactionsAsync();
            IsTransactionsHistoryPopupOpen = true;
        }

        public void CloseTransactionsHistoryPopup()
        {
            IsTransactionsHistoryPopupOpen = false;
        }

        public void EditSupplier(SupplierDTO supplier)
        {
            if (supplier == null) return;

            SelectedSupplier = supplier;
            IsNewSupplier = false;
            ShowSupplierPopup();
        }
        #endregion


        protected override async Task LoadDataAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                // Already executing an operation, skip this one
                Debug.WriteLine("LoadDataAsync skipped - already in progress");
                return;
            }

            // Create a new CancellationTokenSource for this operation
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                try
                {
                    // Add a timeout for the operation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    var suppliers = await _supplierService.GetAllAsync();

                    if (!linkedCts.Token.IsCancellationRequested)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation was canceled");
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error loading suppliers", ex);
                }
            }
            finally
            {
                IsSaving = false;
                _operationLock.Release();
            }
        }


        private async Task LoadSupplierTransactionsAsync()
        {
            if (SelectedSupplier == null) return;

            if (!await _operationLock.WaitAsync(0))
            {
                // Already executing an operation, skip this one
                Debug.WriteLine("LoadSupplierTransactionsAsync skipped - already in progress");
                return;
            }

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                try
                {
                    // Save the selected supplier ID in case it changes during the async operation
                    var supplierId = SelectedSupplier.SupplierId;

                    var transactions = await _supplierService.GetSupplierTransactionsAsync(supplierId);

                    // Check if the supplier is still selected
                    if (SelectedSupplier?.SupplierId == supplierId)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // Sort transactions by date descending for better presentation
                            SupplierTransactions = new ObservableCollection<SupplierTransactionDTO>(
                                transactions.OrderByDescending(t => t.TransactionDate));
                        });
                    }
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error loading transactions", ex);
                }
            }
            finally
            {
                IsSaving = false;
                _operationLock.Release();
            }
        }

        private void AddNew()
        {
            SelectedSupplier = new SupplierDTO
            {
                IsActive = true,
                Balance = 0,
                CreatedAt = DateTime.Now
            };
            IsNewSupplier = true;
            ShowSupplierPopup();
        }

        private async Task SaveAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Save operation already in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                if (SelectedSupplier == null) return;

                if (string.IsNullOrWhiteSpace(SelectedSupplier.Name))
                {
                    ShowTemporaryErrorMessage("Supplier name is required.");
                    return;
                }

                try
                {
                    if (SelectedSupplier.SupplierId == 0)
                    {
                        // Create new supplier
                        var savedSupplier = await _supplierService.CreateAsync(SelectedSupplier);

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Suppliers.Add(savedSupplier);
                            SelectedSupplier = savedSupplier;
                        });
                    }
                    else
                    {
                        // Update existing supplier
                        await _supplierService.UpdateAsync(SelectedSupplier);

                        // Update the collection
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            int index = -1;
                            for (int i = 0; i < Suppliers.Count; i++)
                            {
                                if (Suppliers[i].SupplierId == SelectedSupplier.SupplierId)
                                {
                                    index = i;
                                    break;
                                }
                            }

                            if (index != -1)
                            {
                                Suppliers[index] = SelectedSupplier;
                            }
                            else
                            {
                                // If supplier not found in collection, refresh all
                                LoadDataAsync();
                            }
                        });
                    }

                    // Close popup and show success message
                    CloseSupplierPopup();

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Supplier saved successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error saving supplier", ex);
                }
            }
            finally
            {
                IsSaving = false;
                _operationLock.Release();
            }
        }

        private async Task DeleteAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Delete operation already in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                if (SelectedSupplier == null) return;

                try
                {
                    if (MessageBox.Show("Are you sure you want to delete this supplier?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        // Store the ID for reference
                        int supplierIdToDelete = SelectedSupplier.SupplierId;

                        // Close any open popups
                        CloseSupplierPopup();
                        CloseTransactionPopup();
                        CloseTransactionsHistoryPopup();

                        await _supplierService.DeleteAsync(supplierIdToDelete);

                        // Remove from local collection
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var supplierToRemove = Suppliers.FirstOrDefault(s => s.SupplierId == supplierIdToDelete);
                            if (supplierToRemove != null)
                            {
                                Suppliers.Remove(supplierToRemove);
                            }

                            // Clear selection
                            SelectedSupplier = null;
                        });

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show("Supplier deleted successfully.", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error deleting supplier", ex);

                    // If error deleting, offer to mark inactive instead
                    if (ex.Message.Contains("Cannot delete") || ex.Message.Contains("references"))
                    {
                        var result = MessageBox.Show("Would you like to mark this supplier as inactive instead?",
                            "Mark Inactive", MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes && SelectedSupplier != null)
                        {
                            SelectedSupplier.IsActive = false;
                            await SaveAsync();
                        }
                    }
                }
            }
            finally
            {
                IsSaving = false;
                _operationLock.Release();
            }
        }

        private async Task SearchSuppliersAsync()
        {
            // Use a throttling approach for search
            await Task.Delay(300); // Wait for user to finish typing

            if (!await _operationLock.WaitAsync(0))
            {
                // Skip if another operation is in progress
                return;
            }

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                try
                {
                    if (string.IsNullOrWhiteSpace(SearchText))
                    {
                        await LoadDataAsync();
                        return;
                    }

                    var suppliers = await _supplierService.GetByNameAsync(SearchText);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                    });
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error searching suppliers", ex);
                }
            }
            finally
            {
                IsSaving = false;
                _operationLock.Release();
            }
        }

        private async Task AddPaymentAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                ShowTemporaryErrorMessage("Payment operation already in progress. Please wait.");
                return;
            }

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;

                if (SelectedSupplier == null || PaymentAmount <= 0)
                {
                    ShowTemporaryErrorMessage("Please select a supplier and enter a valid payment amount.");
                    return;
                }

                try
                {
                    // Check drawer first
                    var drawer = await _drawerService.GetCurrentDrawerAsync();
                    if (drawer == null)
                    {
                        ShowTemporaryErrorMessage("No active cash drawer. Please open a drawer first.");
                        return;
                    }

                    // Validate sufficient funds
                    if (PaymentAmount > drawer.CurrentBalance)
                    {
                        ShowTemporaryErrorMessage("Insufficient funds in drawer.");
                        return;
                    }

                    var reference = $"PAY-{DateTime.Now:yyyyMMddHHmmss}";
                    var supplierTransaction = new SupplierTransactionDTO
                    {
                        SupplierId = SelectedSupplier.SupplierId,
                        SupplierName = SelectedSupplier.Name,
                        Amount = -PaymentAmount,
                        TransactionType = "Payment",
                        Notes = Notes,
                        Reference = reference,
                        TransactionDate = DateTime.Now
                    };

                    // Store if history popup was open before
                    bool wasHistoryOpen = IsTransactionsHistoryPopupOpen;

                    // Close transaction popup first
                    CloseTransactionPopup();

                    // Close history popup if open
                    if (wasHistoryOpen)
                    {
                        CloseTransactionsHistoryPopup();
                    }

                    // Let the service handle the transaction
                    await _supplierService.AddTransactionAsync(supplierTransaction, true);

                    // Reload supplier data to update balance
                    await LoadDataAsync();

                    // Refresh the selected supplier
                    if (SelectedSupplier != null)
                    {
                        var refreshedSupplier = await _supplierService.GetByIdAsync(SelectedSupplier.SupplierId);
                        if (refreshedSupplier != null)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                // Update the SelectedSupplier 
                                SelectedSupplier = refreshedSupplier;

                                // Update the supplier in the collection
                                var index = -1;
                                for (int i = 0; i < Suppliers.Count; i++)
                                {
                                    if (Suppliers[i].SupplierId == refreshedSupplier.SupplierId)
                                    {
                                        index = i;
                                        break;
                                    }
                                }

                                if (index != -1)
                                {
                                    Suppliers[index] = refreshedSupplier;
                                    // Force collection change notification
                                    var temp = new ObservableCollection<SupplierDTO>(Suppliers);
                                    Suppliers = temp;
                                }
                            });
                        }
                    }

                    // Reopen transactions history if it was open
                    if (wasHistoryOpen)
                    {
                        await Task.Delay(100); // Small delay to ensure UI updates
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ShowTransactionsHistoryPopup();
                        });
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Payment recorded successfully and cash drawer updated.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync("Error recording payment", ex);
                }
            }
            finally
            {
                IsSaving = false;
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
                ShowTemporaryErrorMessage("Database is busy processing another request. Please try again in a moment.");
            }
            else if (ex.Message.Contains("entity with the specified primary key") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("entity with the specified primary key")))
            {
                ShowTemporaryErrorMessage("Requested record not found. It may have been deleted.");
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ShowTemporaryErrorMessage("Database connection lost. Please check your connection and try again.");
            }
            else
            {
                ShowTemporaryErrorMessage($"{context}: {ex.Message}");
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

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _operationLock?.Dispose();
                UnsubscribeFromEvents();
                _isDisposed = true;
            }
            base.Dispose();
        }
    }
}