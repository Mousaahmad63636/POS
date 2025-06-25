using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.Application.Mappings;

namespace QuickTechSystems.ViewModels.Customer
{
    public class CustomerViewModel : ViewModelBase
    {
        private readonly ICustomerService _customerService;
        private readonly Dictionary<string, object> _operationCache;
        private readonly HashSet<int> _processingOperations;
        private readonly Queue<Func<Task>> _operationQueue;
        private readonly Dictionary<int, CustomerDTO> _customerLookup;

        private ObservableCollection<CustomerDTO> _customers;
        private ObservableCollection<CustomerPaymentDTO> _customerPayments;
        private CustomerDTO _selectedCustomer;
        private CustomerDTO _editingCustomer;
        private CustomerPaymentDTO _selectedPayment;
        private CustomerPaymentDTO _editingPayment;
        private string _searchText;
        private bool _isAddingCustomer;
        private bool _isEditingCustomer;
        private bool _isEditingBalance;
        private bool _isSettlingDebt;
        private bool _isShowingHistory;
        private bool _isEditingPayment;
        private decimal _balanceAdjustment;
        private decimal _newBalance;
        private decimal _paymentAmount;
        private string _paymentNotes;
        private string _paymentMethod;
        private string _adjustmentReason;
        private bool _isSettingNewBalance;

        public ObservableCollection<CustomerDTO> Customers
        {
            get => _customers;
            set => SetProperty(ref _customers, value);
        }

        public ObservableCollection<CustomerPaymentDTO> CustomerPayments
        {
            get => _customerPayments;
            set => SetProperty(ref _customerPayments, value);
        }

        public CustomerDTO SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                if (SetProperty(ref _selectedCustomer, value))
                {
                    OnSelectedCustomerChanged();
                }
            }
        }

        public CustomerDTO EditingCustomer
        {
            get => _editingCustomer;
            set => SetProperty(ref _editingCustomer, value);
        }

        public CustomerPaymentDTO SelectedPayment
        {
            get => _selectedPayment;
            set => SetProperty(ref _selectedPayment, value);
        }

        public CustomerPaymentDTO EditingPayment
        {
            get => _editingPayment;
            set => SetProperty(ref _editingPayment, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterCustomers();
                }
            }
        }

        public bool IsAddingCustomer
        {
            get => _isAddingCustomer;
            set => SetProperty(ref _isAddingCustomer, value);
        }

        public bool IsEditingCustomer
        {
            get => _isEditingCustomer;
            set => SetProperty(ref _isEditingCustomer, value);
        }

        public bool IsEditingBalance
        {
            get => _isEditingBalance;
            set => SetProperty(ref _isEditingBalance, value);
        }

        public bool IsSettlingDebt
        {
            get => _isSettlingDebt;
            set => SetProperty(ref _isSettlingDebt, value);
        }

        public bool IsShowingHistory
        {
            get => _isShowingHistory;
            set => SetProperty(ref _isShowingHistory, value);
        }

        public bool IsEditingPayment
        {
            get => _isEditingPayment;
            set => SetProperty(ref _isEditingPayment, value);
        }

        public decimal BalanceAdjustment
        {
            get => _balanceAdjustment;
            set => SetProperty(ref _balanceAdjustment, value);
        }

        public decimal NewBalance
        {
            get => _newBalance;
            set => SetProperty(ref _newBalance, value);
        }

        public bool IsSettingNewBalance
        {
            get => _isSettingNewBalance;
            set => SetProperty(ref _isSettingNewBalance, value);
        }

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set => SetProperty(ref _paymentAmount, value);
        }

        public string PaymentNotes
        {
            get => _paymentNotes;
            set => SetProperty(ref _paymentNotes, value);
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            set => SetProperty(ref _paymentMethod, value);
        }

        public string AdjustmentReason
        {
            get => _adjustmentReason;
            set => SetProperty(ref _adjustmentReason, value);
        }

        public ICommand AddCustomerCommand { get; private set; }
        public ICommand EditCustomerCommand { get; private set; }
        public ICommand SaveCustomerCommand { get; private set; }
        public ICommand CancelEditCommand { get; private set; }
        public ICommand DeleteCustomerCommand { get; private set; }
        public ICommand EditBalanceCommand { get; private set; }
        public ICommand SaveBalanceCommand { get; private set; }
        public ICommand SettleDebtCommand { get; private set; }
        public ICommand ProcessPaymentCommand { get; private set; }
        public ICommand ShowHistoryCommand { get; private set; }
        public ICommand HideHistoryCommand { get; private set; }
        public ICommand EditPaymentCommand { get; private set; }
        public ICommand SavePaymentCommand { get; private set; }
        public ICommand DeletePaymentCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        public CustomerViewModel(
            IEventAggregator eventAggregator,
            ICustomerService customerService,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _customerService = customerService;
            _operationCache = new Dictionary<string, object>();
            _processingOperations = new HashSet<int>();
            _operationQueue = new Queue<Func<Task>>();
            _customerLookup = new Dictionary<int, CustomerDTO>();

            _customers = new ObservableCollection<CustomerDTO>();
            _customerPayments = new ObservableCollection<CustomerPaymentDTO>();
            _searchText = string.Empty;
            _paymentNotes = string.Empty;
            _paymentMethod = "Cash";
            _adjustmentReason = string.Empty;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            AddCustomerCommand = new RelayCommand(_ => StartAddCustomer());
            EditCustomerCommand = new RelayCommand(_ => StartEditCustomer(), _ => SelectedCustomer != null);
            SaveCustomerCommand = new RelayCommand(async _ => await SaveCustomerAsync());
            CancelEditCommand = new RelayCommand(_ => CancelEdit());
            DeleteCustomerCommand = new RelayCommand(async _ => await DeleteCustomerAsync(), _ => SelectedCustomer != null);
            EditBalanceCommand = new RelayCommand(_ => StartEditBalance(), _ => SelectedCustomer != null);
            SaveBalanceCommand = new RelayCommand(async _ => await SaveBalanceAsync());
            SettleDebtCommand = new RelayCommand(_ => StartSettleDebt(), _ => SelectedCustomer?.Balance > 0);
            ProcessPaymentCommand = new RelayCommand(async _ => await ProcessPaymentAsync());
            ShowHistoryCommand = new RelayCommand(async _ => await ShowPaymentHistoryAsync(), _ => SelectedCustomer != null);
            HideHistoryCommand = new RelayCommand(_ => IsShowingHistory = false);
            EditPaymentCommand = new RelayCommand(_ => StartEditPayment(), _ => SelectedPayment != null);
            SavePaymentCommand = new RelayCommand(async _ => await SavePaymentAsync());
            DeletePaymentCommand = new RelayCommand(async _ => await DeletePaymentAsync(), _ => SelectedPayment != null);
            RefreshCommand = new RelayCommand(async _ => await LoadDataAsync());
        }

        protected override async Task LoadDataImplementationAsync()
        {
            await ExecuteWithOperationLockAsync("LoadCustomers", async () =>
            {
                var customers = await _customerService.GetAllAsync();
                var customerList = customers.ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _customerLookup.Clear();
                    Customers.Clear();

                    foreach (var customer in customerList)
                    {
                        Customers.Add(customer);
                        _customerLookup[customer.CustomerId] = customer;
                    }
                });
            });
        }

        private void OnSelectedCustomerChanged()
        {
            if (SelectedCustomer != null && !_processingOperations.Contains(SelectedCustomer.CustomerId))
            {
                Task.Run(async () => await LoadCustomerPaymentsAsync(SelectedCustomer.CustomerId));
            }
        }

        private async Task LoadCustomerPaymentsAsync(int customerId)
        {
            try
            {
                _processingOperations.Add(customerId);

                var payments = await _customerService.GetCustomerPaymentsAsync(customerId);
                var paymentList = payments?.ToList() ?? new List<CustomerPaymentDTO>();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CustomerPayments.Clear();
                    foreach (var payment in paymentList)
                    {
                        CustomerPayments.Add(payment);
                    }
                });
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error loading customer payments", ex);
            }
            finally
            {
                _processingOperations.Remove(customerId);
            }
        }

        private void StartAddCustomer()
        {
            EditingCustomer = new CustomerDTO
            {
                Name = string.Empty,
                Phone = string.Empty,
                Email = string.Empty,
                Address = string.Empty,
                Balance = 0,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            IsAddingCustomer = true;
        }

        private void StartEditCustomer()
        {
            if (SelectedCustomer == null) return;

            EditingCustomer = new CustomerDTO
            {
                CustomerId = SelectedCustomer.CustomerId,
                Name = SelectedCustomer.Name,
                Phone = SelectedCustomer.Phone,
                Email = SelectedCustomer.Email,
                Address = SelectedCustomer.Address,
                Balance = SelectedCustomer.Balance,
                IsActive = SelectedCustomer.IsActive,
                CreatedAt = SelectedCustomer.CreatedAt,
                UpdatedAt = DateTime.Now
            };
            IsEditingCustomer = true;
        }

        private async Task SaveCustomerAsync()
        {
            if (EditingCustomer == null) return;

            await ExecuteWithOperationLockAsync("SaveCustomer", async () =>
            {
                if (string.IsNullOrWhiteSpace(EditingCustomer.Name))
                {
                    ShowTemporaryErrorMessage("Customer name is required");
                    return;
                }

                if (IsAddingCustomer)
                {
                    var createdCustomer = await _customerService.CreateAsync(EditingCustomer);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Customers.Add(createdCustomer);
                        _customerLookup[createdCustomer.CustomerId] = createdCustomer;
                        SelectedCustomer = createdCustomer;
                    });
                    await ShowSuccessMessage("Customer added successfully");
                }
                else
                {
                    await _customerService.UpdateAsync(EditingCustomer);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var existingCustomer = Customers.FirstOrDefault(c => c.CustomerId == EditingCustomer.CustomerId);
                        if (existingCustomer != null)
                        {
                            var index = Customers.IndexOf(existingCustomer);
                            Customers[index] = EditingCustomer;
                            _customerLookup[EditingCustomer.CustomerId] = EditingCustomer;
                            SelectedCustomer = EditingCustomer;
                        }
                    });
                    await ShowSuccessMessage("Customer updated successfully");
                }

                CancelEdit();
            });
        }

        private void CancelEdit()
        {
            EditingCustomer = null;
            EditingPayment = null;
            IsAddingCustomer = false;
            IsEditingCustomer = false;
            IsEditingBalance = false;
            IsSettlingDebt = false;
            IsShowingHistory = false;
            IsEditingPayment = false;
            IsSettingNewBalance = false;
            BalanceAdjustment = 0;
            NewBalance = 0;
            PaymentAmount = 0;
            PaymentNotes = string.Empty;
            PaymentMethod = "Cash";
            AdjustmentReason = string.Empty;
        }

        private async Task DeleteCustomerAsync()
        {
            if (SelectedCustomer == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete customer '{SelectedCustomer.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await ExecuteWithOperationLockAsync("DeleteCustomer", async () =>
                {
                    await _customerService.DeleteAsync(SelectedCustomer.CustomerId);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _customerLookup.Remove(SelectedCustomer.CustomerId);
                        Customers.Remove(SelectedCustomer);
                        SelectedCustomer = null;
                        CustomerPayments.Clear();
                    });
                    await ShowSuccessMessage("Customer deleted successfully");
                });
            }
        }

        private void StartEditBalance()
        {
            if (SelectedCustomer == null) return;
            BalanceAdjustment = 0;
            NewBalance = SelectedCustomer.Balance;
            IsSettingNewBalance = true;
            AdjustmentReason = string.Empty;
            IsEditingBalance = true;
        }

        private async Task SaveBalanceAsync()
        {
            if (SelectedCustomer == null) return;

            await ExecuteWithOperationLockAsync("SaveBalance", async () =>
            {
                if (string.IsNullOrWhiteSpace(AdjustmentReason))
                {
                    ShowTemporaryErrorMessage("Adjustment reason is required");
                    return;
                }

                CustomerDTO updatedCustomer;

                if (IsSettingNewBalance)
                {
                    updatedCustomer = await _customerService.SetBalanceAsync(
                        SelectedCustomer.CustomerId,
                        NewBalance,
                        AdjustmentReason);
                }
                else
                {
                    updatedCustomer = await _customerService.UpdateBalanceAsync(
                        SelectedCustomer.CustomerId,
                        BalanceAdjustment,
                        AdjustmentReason);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var index = Customers.IndexOf(SelectedCustomer);
                    if (index >= 0)
                    {
                        Customers[index] = updatedCustomer;
                        _customerLookup[updatedCustomer.CustomerId] = updatedCustomer;
                        SelectedCustomer = updatedCustomer;
                    }
                });

                if (IsShowingHistory)
                {
                    await LoadCustomerPaymentsAsync(SelectedCustomer.CustomerId);
                }

                await ShowSuccessMessage("Balance updated successfully");
                CancelEdit();
            });
        }

        private void StartSettleDebt()
        {
            if (SelectedCustomer?.Balance <= 0) return;
            PaymentAmount = SelectedCustomer.Balance;
            PaymentNotes = "Debt settlement";
            PaymentMethod = "Cash";
            IsSettlingDebt = true;
        }

        private async Task ProcessPaymentAsync()
        {
            if (SelectedCustomer == null || PaymentAmount <= 0) return;

            await ExecuteWithOperationLockAsync("ProcessPayment", async () =>
            {
                var processedCustomer = await _customerService.ProcessPaymentAsync(
                    SelectedCustomer.CustomerId,
                    PaymentAmount,
                    PaymentNotes,
                    PaymentMethod);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var index = Customers.IndexOf(SelectedCustomer);
                    if (index >= 0)
                    {
                        Customers[index] = processedCustomer;
                        _customerLookup[processedCustomer.CustomerId] = processedCustomer;
                        SelectedCustomer = processedCustomer;
                    }
                });

                if (IsShowingHistory)
                {
                    await LoadCustomerPaymentsAsync(SelectedCustomer.CustomerId);
                }

                await ShowSuccessMessage($"Payment of {PaymentAmount:C} processed successfully");
                CancelEdit();
            });
        }

        private async Task ShowPaymentHistoryAsync()
        {
            if (SelectedCustomer == null) return;

            await ExecuteWithOperationLockAsync("ShowPaymentHistory", async () =>
            {
                await LoadCustomerPaymentsAsync(SelectedCustomer.CustomerId);
                IsShowingHistory = true;
            });
        }

        private void StartEditPayment()
        {
            if (SelectedPayment == null) return;

            EditingPayment = new CustomerPaymentDTO
            {
                PaymentId = SelectedPayment.PaymentId,
                CustomerId = SelectedPayment.CustomerId,
                CustomerName = SelectedPayment.CustomerName,
                Amount = SelectedPayment.Amount,
                PaymentDate = SelectedPayment.PaymentDate,
                PaymentMethod = SelectedPayment.PaymentMethod,
                Notes = SelectedPayment.Notes,
                Status = SelectedPayment.Status,
                CreatedBy = SelectedPayment.CreatedBy
            };
            IsEditingPayment = true;
        }

        private async Task SavePaymentAsync()
        {
            if (EditingPayment == null) return;

            var selectedCustomerId = SelectedCustomer?.CustomerId;
            if (!selectedCustomerId.HasValue) return;

            await ExecuteWithOperationLockAsync("SavePayment", async () =>
            {
                if (EditingPayment.Amount <= 0)
                {
                    ShowTemporaryErrorMessage("Payment amount must be greater than zero");
                    return;
                }

                if (string.IsNullOrWhiteSpace(EditingPayment.PaymentMethod))
                {
                    ShowTemporaryErrorMessage("Payment method is required");
                    return;
                }

                var updatedPayment = await _customerService.UpdatePaymentAsync(EditingPayment);

                // Refresh customer data
                var refreshedCustomers = await _customerService.GetAllAsync();
                var refreshedCustomersList = refreshedCustomers.ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var targetCustomer = refreshedCustomersList.FirstOrDefault(c => c.CustomerId == selectedCustomerId.Value);

                    if (targetCustomer != null)
                    {
                        _customerLookup.Clear();
                        Customers.Clear();

                        foreach (var customer in refreshedCustomersList)
                        {
                            Customers.Add(customer);
                            _customerLookup[customer.CustomerId] = customer;
                        }

                        SelectedCustomer = _customerLookup[selectedCustomerId.Value];
                    }
                });

                await LoadCustomerPaymentsAsync(selectedCustomerId.Value);

                await ShowSuccessMessage("Payment updated successfully");
                CancelEdit();
            });
        }

        private async Task DeletePaymentAsync()
        {
            if (SelectedPayment == null) return;

            var selectedCustomerId = SelectedCustomer?.CustomerId;
            if (!selectedCustomerId.HasValue) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete this payment of {SelectedPayment.Amount:C}?",
                "Confirm Delete Payment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await ExecuteWithOperationLockAsync("DeletePayment", async () =>
                {
                    var deleted = await _customerService.DeletePaymentAsync(
                        SelectedPayment.PaymentId,
                        "Manual deletion by user");

                    if (deleted)
                    {
                        // Refresh customer data
                        var refreshedCustomers = await _customerService.GetAllAsync();
                        var refreshedCustomersList = refreshedCustomers.ToList();

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var targetCustomer = refreshedCustomersList.FirstOrDefault(c => c.CustomerId == selectedCustomerId.Value);

                            if (targetCustomer != null)
                            {
                                _customerLookup.Clear();
                                Customers.Clear();

                                foreach (var customer in refreshedCustomersList)
                                {
                                    Customers.Add(customer);
                                    _customerLookup[customer.CustomerId] = customer;
                                }

                                SelectedCustomer = _customerLookup[selectedCustomerId.Value];
                            }
                        });

                        await LoadCustomerPaymentsAsync(selectedCustomerId.Value);

                        await ShowSuccessMessage("Payment deleted successfully");
                    }
                    else
                    {
                        ShowTemporaryErrorMessage("Failed to delete payment");
                    }
                });
            }
        }

        private void FilterCustomers()
        {
            var allCustomers = _operationCache.ContainsKey("AllCustomers")
                ? (List<CustomerDTO>)_operationCache["AllCustomers"]
                : Customers.ToList();

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Customers.Clear();
                    _customerLookup.Clear();
                    foreach (var customer in allCustomers)
                    {
                        Customers.Add(customer);
                        _customerLookup[customer.CustomerId] = customer;
                    }
                });
                return;
            }

            var filteredCustomers = allCustomers.Where(c =>
                c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Email) && c.Email.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Customers.Clear();
                _customerLookup.Clear();
                foreach (var customer in filteredCustomers)
                {
                    Customers.Add(customer);
                    _customerLookup[customer.CustomerId] = customer;
                }
            });
        }
    }
}