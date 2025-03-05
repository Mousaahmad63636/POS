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
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
namespace QuickTechSystems.WPF.ViewModels
{
    public class CustomerDebtViewModel : ViewModelBase
    {
        private Action<EntityChangedEvent<CustomerDTO>> _customerDebtChangedHandler;
        private readonly ICustomerService _customerService;
        private readonly ITransactionService _transactionService;
        private readonly IUnitOfWork _unitOfWork;
        private ObservableCollection<CustomerDTO> _customersWithDebt;
        private CustomerDTO? _selectedCustomer;
        private decimal _paymentAmount;
        private ObservableCollection<TransactionDTO> _transactionHistory;
        private ObservableCollection<CustomerPaymentDTO> _paymentHistory;
        private string _searchText = string.Empty;
        private decimal _totalAmountLBP;

        public CustomerDebtViewModel(
       ICustomerService customerService,
       ITransactionService transactionService,
       IUnitOfWork unitOfWork,
       IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _customerService = customerService;
            _transactionService = transactionService;
            _unitOfWork = unitOfWork;
            _customersWithDebt = new ObservableCollection<CustomerDTO>();
            _paymentHistory = new ObservableCollection<CustomerPaymentDTO>();
            _transactionHistory = new ObservableCollection<TransactionDTO>();
            _customerDebtChangedHandler = HandleCustomerDebtChanged;
            ProcessPaymentCommand = new AsyncRelayCommand(async _ => await ProcessPaymentAsync());
            ViewTransactionDetailCommand = new RelayCommand(ShowTransactionDetail);
            PrintTransactionCommand = new AsyncRelayCommand<TransactionDTO>(async transaction => await PrintTransactionAsync(transaction));
            SearchCommand = new AsyncRelayCommand(async _ => await SearchCustomersAsync());

            _ = LoadDataAsync();
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
                if (SetProperty(ref _selectedCustomer, value))
                {
                    _ = LoadCustomerDetailsAsync();
                    UpdateTotalAmountLBP();
                }
            }
        }
        private async Task PrintTransactionAsync(TransactionDTO? transaction)
        {
            if (transaction == null) return;

            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true) return;

                var flowDocument = new FlowDocument
                {
                    PageWidth = printDialog.PrintableAreaWidth,
                    ColumnWidth = printDialog.PrintableAreaWidth,
                    FontFamily = new FontFamily("Arial"),
                    PagePadding = new Thickness(20, 0, 20, 0),
                    TextAlignment = TextAlignment.Center,
                    PageHeight = printDialog.PrintableAreaHeight,
                    Foreground = Brushes.Black
                };

                // Header Section
                var header = new Paragraph
                {
                    FontSize = 18,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                header.Inlines.Add("GalaxyNet\n");
                header.Inlines.Add(new Run("Your partner in all your IT problems\n 81 20 77 06\n 03 65 74 64 \n ")
                {
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
                flowDocument.Blocks.Add(header);
                flowDocument.Blocks.Add(CreateDivider());

                // Transaction Metadata
                var metaTable = new Table { FontSize = 11, CellSpacing = 0 };
                metaTable.Columns.Add(new TableColumn { Width = new GridLength(120) });
                metaTable.Columns.Add(new TableColumn { Width = GridLength.Auto });
                metaTable.RowGroups.Add(new TableRowGroup());
                AddMetaRow(metaTable, "TRX #:", transaction.TransactionId.ToString());
                AddMetaRow(metaTable, "DATE:", transaction.TransactionDate.ToString("MM/dd/yyyy hh:mm tt"));
                AddMetaRow(metaTable, "CUSTOMER:", transaction.CustomerName);
                AddMetaRow(metaTable, "TYPE:", transaction.TransactionType.ToString());
                flowDocument.Blocks.Add(metaTable);
                flowDocument.Blocks.Add(CreateDivider());

                // Payment Details Table
                var paymentTable = new Table { FontSize = 11, CellSpacing = 0 };
                paymentTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
                paymentTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                paymentTable.RowGroups.Add(new TableRowGroup());

                if (transaction.TransactionType == TransactionType.Payment)
                {
                    // For Payment transactions, show payment amount and remaining balance
                    AddTotalRow(paymentTable, "PAYMENT USD:", transaction.TotalAmount.ToString("C2"), true);
                    decimal lbpAmount = CurrencyHelper.ConvertToLBP(transaction.TotalAmount);
                    AddTotalRow(paymentTable, "PAYMENT LBP:", CurrencyHelper.FormatLBP(lbpAmount), true);

                    // Add remaining balance
                    if (transaction.Balance > 0)
                    {
                        AddTotalRow(paymentTable, "REMAINING BALANCE USD:", transaction.Balance.ToString("C2"), true);
                        decimal balanceLbp = CurrencyHelper.ConvertToLBP(transaction.Balance);
                        AddTotalRow(paymentTable, "REMAINING BALANCE LBP:", CurrencyHelper.FormatLBP(balanceLbp), true);
                    }
                }
                else if (transaction.TransactionType == TransactionType.Sale)
                {
                    // For Sale transactions, show only the total amount
                    AddTotalRow(paymentTable, "TOTAL USD:", transaction.TotalAmount.ToString("C2"), true);
                    decimal lbpAmount = CurrencyHelper.ConvertToLBP(transaction.TotalAmount);
                    AddTotalRow(paymentTable, "TOTAL LBP:", CurrencyHelper.FormatLBP(lbpAmount), true);
                }

                flowDocument.Blocks.Add(paymentTable);
                flowDocument.Blocks.Add(CreateDivider());

                printDialog.PrintDocument(
                    ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                    $"{transaction.TransactionType} Receipt {transaction.TransactionId}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error printing receipt: {ex.Message}", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TableCell CreateCell(string text, FontWeight fontWeight = default, TextAlignment alignment = TextAlignment.Left)
        {
            return new TableCell(new Paragraph(new Run(text)
            {
                Foreground = Brushes.Black
            })
            {
                FontWeight = fontWeight,
                TextAlignment = alignment,
                Margin = new Thickness(1)
            });
        }

        private void AddMetaRow(Table table, string label, string value)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(label, FontWeights.ExtraBold));
            row.Cells.Add(CreateCell(value, FontWeights.Bold));
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddTotalRow(Table table, string label, string value, bool isBold = false)
        {
            var row = new TableRow();
            var weight = isBold ? FontWeights.ExtraBold : FontWeights.Bold;
            row.Cells.Add(CreateCell(label, weight, TextAlignment.Left));
            row.Cells.Add(CreateCell(value, weight, TextAlignment.Right));
            table.RowGroups[0].Rows.Add(row);
        }

        private BlockUIContainer CreateDivider()
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                Margin = new Thickness(0, 2, 0, 2)
            });
        }


        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set => SetProperty(ref _paymentAmount, value);
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

        public ICommand ProcessPaymentCommand { get; }
        public ICommand ViewTransactionDetailCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand PrintTransactionCommand { get; }

        private async Task SearchCustomersAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                await LoadDataAsync();
                return;
            }

            try
            {
                var customers = await _customerService.GetByNameAsync(SearchText);
                CustomersWithDebt = new ObservableCollection<CustomerDTO>(
                    customers.Where(c => c.Balance > 0));
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error searching customers: {ex.Message}");
            }
        }

        private void UpdateTotalAmountLBP()
        {
            if (SelectedCustomer != null)
            {
                _totalAmountLBP = CurrencyHelper.ConvertToLBP(SelectedCustomer.Balance);
                OnPropertyChanged(nameof(TotalAmountLBP));
            }
        }

        private void ShowTransactionDetail(object? parameter)
        {
            if (parameter is TransactionDTO transaction)
            {
                try
                {
                    // Get the current application's main window
                    var mainWindow = System.Windows.Application.Current.MainWindow;

                    // Create and show the detail window with proper ownership
                    var detailWindow = new TransactionDetailWindow(transaction)
                    {
                        Owner = mainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    detailWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error showing transaction details: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                var customers = await _customerService.GetCustomersWithDebtAsync();
                CustomersWithDebt = new ObservableCollection<CustomerDTO>(customers);
                UpdateTotalAmountLBP();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading customers: {ex.Message}");
            }
        }

        private async Task LoadCustomerDetailsAsync()
        {
            if (SelectedCustomer == null) return;

            try
            {
                var payments = await _customerService.GetPaymentHistoryAsync(SelectedCustomer.CustomerId);
                PaymentHistory = new ObservableCollection<CustomerPaymentDTO>(payments);

                var transactions = await _transactionService.GetByCustomerAsync(SelectedCustomer.CustomerId);
                TransactionHistory = new ObservableCollection<TransactionDTO>(
                    transactions.OrderByDescending(t => t.TransactionDate)
                );
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading customer details: {ex.Message}");
            }
        }

        private async Task ProcessPaymentAsync()
        {
            if (SelectedCustomer == null)
            {
                await ShowErrorMessageAsync("Please select a customer");
                return;
            }

            if (PaymentAmount <= 0)
            {
                await ShowErrorMessageAsync("Please enter a valid payment amount");
                return;
            }

            try
            {
                using (var transaction = await _unitOfWork.BeginTransactionAsync())
                {
                    try
                    {
                        var customer = await _unitOfWork.Customers.GetByIdAsync(SelectedCustomer.CustomerId);
                        if (customer == null)
                            throw new InvalidOperationException("Customer not found");

                        // Ensure the payment does not exceed the current balance
                        if (PaymentAmount > customer.Balance)
                        {
                            await ShowErrorMessageAsync($"Payment amount cannot exceed the current balance of {customer.Balance:C2}");
                            return;
                        }

                        customer.Balance -= PaymentAmount;

                        // Create CustomerPayment with required Customer reference
                        var payment = new CustomerPayment
                        {
                            CustomerId = SelectedCustomer.CustomerId,
                            Customer = customer,  // Set the Customer reference
                            Amount = PaymentAmount,
                            PaymentDate = DateTime.Now,
                            PaymentMethod = "Cash",
                            Notes = "Debt payment"
                        };

                        await _unitOfWork.Context.Set<CustomerPayment>().AddAsync(payment);
                        await _unitOfWork.Customers.UpdateAsync(customer);
                        await _unitOfWork.SaveChangesAsync();

                        var paymentTransaction = new TransactionDTO
                        {
                            TransactionDate = DateTime.Now,
                            CustomerId = SelectedCustomer.CustomerId,
                            CustomerName = SelectedCustomer.Name,
                            TotalAmount = PaymentAmount,
                            PaidAmount = PaymentAmount,
                            Balance = customer.Balance,
                            TransactionType = TransactionType.Payment,
                            Status = TransactionStatus.Completed,
                            PaymentMethod = "Cash",
                            Details = new ObservableCollection<TransactionDetailDTO>()
                        };

                        await _transactionService.ProcessPaymentTransactionAsync(paymentTransaction);

                        await transaction.CommitAsync();

                        // Clear the form and input fields
                        PaymentAmount = 0; // Clear the payment amount input
                        SearchText = string.Empty; // Clear the search text if applicable

                        // Reload data to reflect the updated balance
                        await LoadDataAsync();
                        await LoadCustomerDetailsAsync();

                        MessageBox.Show($"Payment of {paymentTransaction.TotalAmount:C2} processed successfully. Remaining balance: {customer.Balance:C2}", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error processing payment: {ex.Message}");
            }
        }


        protected override void SubscribeToEvents()
        {
            // Subscribe to CustomerDTO and TransactionDTO events
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(async evt =>
            {
                if (evt.Action == "Create" && evt.Entity.Balance > 0)
                {
                    await LoadDataAsync();
                }
            });
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerDebtChangedHandler);
        }

        private async void HandleCustomerDebtChanged(EntityChangedEvent<CustomerDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                switch (evt.Action)
                {
                    case "Update":
                        // Update specific customer's debt
                        var existingCustomer = CustomersWithDebt
                            .FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);

                        if (existingCustomer != null)
                        {
                            existingCustomer.Balance += evt.Entity.Balance;
                        }
                        else
                        {
                            // If customer not in debt list, reload entire data
                            await LoadDataAsync();
                        }
                        break;
                    case "Create":
                        await LoadDataAsync();
                        break;
                }
            });
        }

    }
}