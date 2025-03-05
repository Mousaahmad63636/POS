using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using QuickTechSystems.Application.Events;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using QuickTechSystems.Infrastructure.Data;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Infrastructure.Repositories;
using System.Windows.Media.Animation;
using QuickTechSystems.Domain.Entities;
using System.Diagnostics;
using System.Windows.Shapes;
using System.Windows.Controls;

using AutoMapper;
using QuickTechSystems.Application.Helpers;

namespace QuickTechSystems.WPF.ViewModels
{
    public class MonthlySubscriptionViewModel : ViewModelBase
    {
        private readonly ICustomerSubscriptionService _subscriptionService;
        private ObservableCollection<CustomerSubscriptionDTO> _customers;
        private CustomerSubscriptionDTO? _selectedCustomer;
        private ObservableCollection<CounterHistoryDTO> _counterHistory;
        private ObservableCollection<SubscriptionPaymentDTO> _paymentHistory;
        private readonly IMonthlySubscriptionSettingsService _settingsService;
        private readonly ISubscriptionTypeService _subscriptionTypeService;
        private MonthlySubscriptionSettingsDTO _settings;
        private ObservableCollection<SubscriptionTypeDTO> _subscriptionTypes;
        private decimal _defaultUnitPrice;
        private decimal _consumption;
        private decimal _additionalFees;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private string _searchText = string.Empty;
        private SubscriptionTypeDTO _selectedSubscriptionType;
        private readonly Action<MeterReadingUpdatedEvent> _meterReadingUpdatedHandler;
        private decimal _newCounter;
        private bool _isEditing;
        private decimal _additionalCharge;
        private decimal _paymentAmount;
        private string _paymentNotes = string.Empty;
        private decimal _baseCharge;
        private decimal _totalBill;
        private readonly IUnitOfWork _unitOfWork;
        private decimal _newReading;
        private string _unitRate = string.Empty;
        private int _selectedYear;
        private int _selectedMonth;
        private decimal _totalConsumption;
        private decimal _totalIncome;
        private decimal _totalExpenses;
        private decimal _totalRevenue;
        private DateTime _selectedBillingDate = DateTime.Now;
        private decimal _currentMonthBill;
        private decimal _paidAmount;
        private decimal _remainingBalance;
        private readonly IMapper _mapper;
        private bool _isNewCustomerDialogOpen;
        private CustomerSubscriptionDTO _newCustomer;
        private DateTime _meterReadingDate = DateTime.Now;
        public decimal TotalBillLBP => CurrencyHelper.ConvertToLBP(TotalBill);

        private ObservableCollection<int> _availableYears;
        public decimal Consumption
        {
            get => _consumption;
            private set => SetProperty(ref _consumption, value);
        }
        public decimal AdditionalFees
        {
            get => _additionalFees;
            set
            {
                if (SetProperty(ref _additionalFees, value))
                {
                    CalculateBill();
                }
            }
        }
        public DateTime MeterReadingDate
        {
            get => _meterReadingDate;
            set => SetProperty(ref _meterReadingDate, value);
        }
        public MonthlySubscriptionViewModel(
                ICustomerSubscriptionService subscriptionService,
                IMonthlySubscriptionSettingsService settingsService,
                ISubscriptionTypeService subscriptionTypeService,
                IDbContextFactory<ApplicationDbContext> contextFactory,
                IEventAggregator eventAggregator,
                IMapper mapper,
                IUnitOfWork unitOfWork) : base(eventAggregator)  // Add IUnitOfWork parameter
        {

            _subscriptionService = subscriptionService;
            _contextFactory = contextFactory;
            _settingsService = settingsService;
            _subscriptionTypeService = subscriptionTypeService;
            _unitOfWork = unitOfWork;  // Initialize the field properly
            var now = DateTime.Now;
            _selectedYear = now.Year;
            _selectedMonth = now.Month;
            _mapper = mapper;
            _selectedBillingDate = new DateTime(_selectedYear, _selectedMonth, 1);

            _selectedYear = DateTime.Now.Year;
            _selectedMonth = DateTime.Now.Month;

            _customers = new ObservableCollection<CustomerSubscriptionDTO>();
            _counterHistory = new ObservableCollection<CounterHistoryDTO>();
            _paymentHistory = new ObservableCollection<SubscriptionPaymentDTO>();
            _subscriptionTypes = new ObservableCollection<SubscriptionTypeDTO>();
            _settings = new MonthlySubscriptionSettingsDTO();

            _availableYears = new ObservableCollection<int>(
                Enumerable.Range(DateTime.Now.Year - 4, 5).Reverse());


            _meterReadingUpdatedHandler = HandleMeterReadingUpdated;
            SubscribeToEvents();

            InitializeCommands();
            _ = LoadDataAsync();
            _ = LoadStatisticsAsync();
        }


        public DateTime SelectedBillingDate
        {
            get => _selectedBillingDate;
            set
            {
                if (SetProperty(ref _selectedBillingDate, value))
                {
                    _ = LoadPaymentDetailsAsync();
                }
            }
        }
        public CustomerSubscriptionDTO NewCustomer
        {
            get => _newCustomer;
            set => SetProperty(ref _newCustomer, value);
        }
        public decimal TotalBill
        {
            get => _totalBill;
            set
            {
                if (SetProperty(ref _totalBill, value))
                {
                    OnPropertyChanged(nameof(TotalBillLBP));
                }
            }
        }
        public decimal CurrentMonthBill
        {
            get => _currentMonthBill;
            set => SetProperty(ref _currentMonthBill, value);
        }

        public decimal PaidAmount
        {
            get => _paidAmount;
            set => SetProperty(ref _paidAmount, value);
        }

        public decimal RemainingBalance
        {
            get => _remainingBalance;
            set => SetProperty(ref _remainingBalance, value);
        }
        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (SetProperty(ref _selectedYear, value))
                {
                    UpdateSelectedDate();
                }
            }
        }
        private async Task LoadPaymentDetailsAsync()
        {
            if (SelectedCustomer == null) return;

            try
            {
                using (var context = await _contextFactory.CreateDbContextAsync())
                using (var transaction = await context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // Get the first and last day of selected month
                        var firstDayOfMonth = new DateTime(SelectedBillingDate.Year, SelectedBillingDate.Month, 1);
                        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                        // Get all bill records for the selected month
                        var monthlyReadings = await context.CounterHistories
                            .Where(h => h.CustomerSubscriptionId == SelectedCustomer.Id &&
                                        h.RecordDate >= firstDayOfMonth &&
                                        h.RecordDate <= lastDayOfMonth)
                            .ToListAsync();

                        // Calculate total bill for the month
                        CurrentMonthBill = monthlyReadings.Sum(r => r.BillAmount);

                        // Get payments for selected month
                        var selectedMonthPayments = await context.SubscriptionPayments
                            .Where(p => p.CustomerSubscriptionId == SelectedCustomer.Id &&
                                        p.PaymentDate >= firstDayOfMonth &&
                                        p.PaymentDate <= lastDayOfMonth)
                            .ToListAsync();

                        PaymentHistory = new ObservableCollection<SubscriptionPaymentDTO>(
                            _mapper.Map<IEnumerable<SubscriptionPaymentDTO>>(selectedMonthPayments));

                        PaidAmount = selectedMonthPayments.Sum(p => p.Amount);
                        RemainingBalance = CurrentMonthBill - PaidAmount;

                        await transaction.CommitAsync();
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
                await ShowErrorMessageAsync($"Error loading payment details: {ex.Message}");
            }
        }

        public int SelectedMonth
        {
            get => _selectedMonth;
            set
            {
                if (value > 0 && value <= 12 && SetProperty(ref _selectedMonth, value))
                {
                    UpdateSelectedDate();
                }
            }
        }
        public decimal TotalExpenses
        {
            get => _totalExpenses;
            set => SetProperty(ref _totalExpenses, value);
        }

        public decimal TotalRevenue
        {
            get => _totalRevenue;
            set => SetProperty(ref _totalRevenue, value);
        }
        public decimal TotalConsumption
        {
            get => _totalConsumption;
            set => SetProperty(ref _totalConsumption, value);
        }

        public decimal TotalIncome
        {
            get => _totalIncome;
            set => SetProperty(ref _totalIncome, value);
        }

        public ObservableCollection<int> AvailableYears
        {
            get => _availableYears;
            set => SetProperty(ref _availableYears, value);
        }


        private void UpdateSelectedDate()
        {
            try
            {
                // Ensure valid month and year
                var month = Math.Max(1, Math.Min(12, _selectedMonth));
                var year = Math.Max(1900, Math.Min(9999, _selectedYear));

                _selectedBillingDate = new DateTime(year, month, 1);
                OnPropertyChanged(nameof(SelectedBillingDate));
                _ = LoadPaymentDetailsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating selected date: {ex.Message}");
                // Reset to current date if there's an error
                var now = DateTime.Now;
                _selectedYear = now.Year;
                _selectedMonth = now.Month;
                _selectedBillingDate = new DateTime(_selectedYear, _selectedMonth, 1);
            }
        }
        public bool IsNewCustomerDialogOpen
        {
            get => _isNewCustomerDialogOpen;
            set => SetProperty(ref _isNewCustomerDialogOpen, value);
        }
        private async Task LoadStatisticsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Get first and last day of selected month
                var startDate = new DateTime(SelectedYear, SelectedMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                // Get readings within date range including subscription type information
                var readings = await context.CounterHistories
                    .Include(ch => ch.CustomerSubscription)
                        .ThenInclude(cs => cs.SubscriptionType)
                    .Where(ch => ch.RecordDate.Year == startDate.Year &&
                                ch.RecordDate.Month == startDate.Month)
                    .ToListAsync();

                decimal totalIncome = 0;
                decimal totalConsumption = 0;

                foreach (var reading in readings)
                {
                    // Calculate consumption
                    decimal consumption = reading.NewCounter - reading.OldCounter;
                    totalConsumption += consumption;

                    // Calculate bill amount
                    decimal billAmount = consumption * reading.PricePerUnit;  // Consumption * Price per unit
                    billAmount += reading.AdditionalFees;  // Add additional fees
                    billAmount += reading.CustomerSubscription.SubscriptionType?.AdditionalCharge ?? 0;  // Add subscription type charge

                    totalIncome += billAmount;
                }

                TotalConsumption = totalConsumption;
                TotalIncome = totalIncome;

                // Get motor expenses
                var motorExpenses = await context.Expenses
                    .Where(e => e.Date.Year == startDate.Year &&
                               e.Date.Month == startDate.Month &&
                               (e.Category == "Motor Expenses" ||
                                e.Category == "Motor Salaries"))
                    .SumAsync(e => e.Amount);

                TotalExpenses = motorExpenses;
                TotalRevenue = TotalIncome - TotalExpenses;

                OnPropertyChanged(nameof(TotalConsumption));
                OnPropertyChanged(nameof(TotalIncome));
                OnPropertyChanged(nameof(TotalExpenses));
                OnPropertyChanged(nameof(TotalRevenue));
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading statistics: {ex.Message}");
            }
        }
        public decimal BaseCharge
        {
            get => _baseCharge;
            set => SetProperty(ref _baseCharge, value);
        }


        public string UnitRate
        {
            get => _unitRate;
            set => SetProperty(ref _unitRate, value);
        }

        public decimal NewReading
        {
            get => _newReading;
            set => SetProperty(ref _newReading, value);
        }
        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<MeterReadingUpdatedEvent>(_meterReadingUpdatedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<MeterReadingUpdatedEvent>(_meterReadingUpdatedHandler);
        }
        private async void HandleMeterReadingUpdated(MeterReadingUpdatedEvent evt)
        {
            if (SelectedCustomer != null && SelectedCustomer.Id == evt.CustomerId)
            {
                // Reload counter history
                var history = await _subscriptionService.GetCustomerHistoryAsync(SelectedCustomer.Id);
                CounterHistory = new ObservableCollection<CounterHistoryDTO>(
                    history.OrderByDescending(h => h.RecordDate));

                // Update customer properties
                SelectedCustomer.NewCounter = evt.NewReading;
                SelectedCustomer.LastBillDate = evt.ReadingDate;
            }
        }
        public DateTime CurrentDate => DateTime.Now;

        public ICommand SaveReadingCommand { get; private set; }
        public SubscriptionTypeDTO SelectedSubscriptionType
        {
            get => _selectedSubscriptionType;
            set => SetProperty(ref _selectedSubscriptionType, value);
        }
        public ObservableCollection<CustomerSubscriptionDTO> Customers
        {
            get => _customers;
            set => SetProperty(ref _customers, value);
        }
        public decimal DefaultUnitPrice
        {
            get => _defaultUnitPrice;
            set => SetProperty(ref _defaultUnitPrice, value);
        }
        public decimal NewCounter
        {
            get => _newCounter;
            set
            {
                if (SetProperty(ref _newCounter, value))
                {
                    CalculateBill();
                }
            }
        }

        public ObservableCollection<SubscriptionTypeDTO> SubscriptionTypes
        {
            get => _subscriptionTypes;
            set => SetProperty(ref _subscriptionTypes, value);
        }

        public MonthlySubscriptionSettingsDTO Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }
        public CustomerSubscriptionDTO? SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                if (SetProperty(ref _selectedCustomer, value))
                {
                    IsEditing = value != null;
                    _ = LoadCustomerDetailsAsync();
                }
            }
        }

        public ObservableCollection<CounterHistoryDTO> CounterHistory
        {
            get => _counterHistory;
            set => SetProperty(ref _counterHistory, value);
        }
        private async Task UpdateOldCounterAsync()
        {
            if (SelectedCustomer == null) return;

            try
            {
                var dialog = new InputDialog("Update Previous Reading", "Enter previous meter reading:");
                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal oldCounter))
                {
                    SelectedCustomer.OldCounter = oldCounter;
                    await _subscriptionService.UpdateAsync(SelectedCustomer);
                    await LoadCustomerDetailsAsync();
                    MessageBox.Show("Previous reading updated successfully", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error updating previous reading: {ex.Message}");
            }
        }


        public ObservableCollection<SubscriptionPaymentDTO> PaymentHistory
        {
            get => _paymentHistory;
            set => SetProperty(ref _paymentHistory, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _ = SearchCustomersAsync();
                }
            }
        }


        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
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

        public ICommand AddCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand UpdateCounterCommand { get; private set; }
        public ICommand ProcessPaymentCommand { get; private set; }
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand AddSubscriptionTypeCommand { get; private set; }
        public ICommand CalculateCommand { get; private set; }
        public ICommand PrintReceiptCommand { get; private set; }
        public ICommand UpdateOldCounterCommand { get; private set; }
        public ICommand ApplyFilterCommand { get; private set; }
        public ICommand SaveHistoryReadingCommand { get; private set; }
        public ICommand ExportHistoryCommand { get; private set; }
        public ICommand PrintAllHistoryCommand { get; private set; }
        public ICommand PrintAllReceiptsCommand { get; private set; }
        public ICommand SubmitNewCustomerCommand { get; private set; }
        public ICommand CancelNewCustomerCommand { get; private set; }
        public ICommand DeleteHistoryReadingCommand { get; private set; }
        public ICommand SetNoFeesCommand { get; private set; }
        private void InitializeCommands()
        {
            AddCommand = new RelayCommand(_ => AddNew());
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
            UpdateCounterCommand = new AsyncRelayCommand(async _ => await UpdateCounterAsync());
            ProcessPaymentCommand = new AsyncRelayCommand(async _ => await ProcessPaymentAsync());
            SaveSettingsCommand = new AsyncRelayCommand(async _ => await SaveSettingsAsync());
            AddSubscriptionTypeCommand = new AsyncRelayCommand(async _ => await AddSubscriptionTypeAsync());
            SaveReadingCommand = new AsyncRelayCommand(async _ => await SaveReadingAsync());
            PrintReceiptCommand = new AsyncRelayCommand<CounterHistoryDTO>(PrintReceiptAsync);
            CalculateCommand = new RelayCommand(_ => CalculateBill());
            SetNoFeesCommand = new RelayCommand(_ => SetNoFees());
            PrintAllReceiptsCommand = new AsyncRelayCommand(async _ => await PrintAllReceiptsAsync());
            PrintAllHistoryCommand = new AsyncRelayCommand(async _ => await PrintAllHistoryAsync());
            ExportHistoryCommand = new AsyncRelayCommand(async _ => await ExportHistoryAsync());
            UpdateOldCounterCommand = new AsyncRelayCommand(async _ => await UpdateOldCounterAsync());
            ApplyFilterCommand = new AsyncRelayCommand(async _ => await LoadStatisticsAsync());
            SaveHistoryReadingCommand = new AsyncRelayCommand<CounterHistoryDTO>(SaveHistoryReadingAsync);
            SubmitNewCustomerCommand = new AsyncRelayCommand(async _ => await SubmitNewCustomerAsync());
            DeleteHistoryReadingCommand = new AsyncRelayCommand<CounterHistoryDTO>(DeleteHistoryReadingAsync);
            CancelNewCustomerCommand = new RelayCommand(_ => CancelNewCustomer());

        }
        private async Task UpdateCustomerSubscriptionType()
        {
            if (SelectedCustomer == null || SelectedSubscriptionType == null) return;

            try
            {
                SelectedCustomer.SubscriptionTypeId = SelectedSubscriptionType.Id;
                SelectedCustomer.AdditionalCharge = SelectedSubscriptionType.AdditionalCharge;
                await _subscriptionService.UpdateAsync(SelectedCustomer);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error updating subscription type: {ex.Message}");
            }
        }
        private void SetNoFees()
        {
            AdditionalFees = 0;
            CalculateBill(); // Recalculate the bill to update totals
        }
        private async Task ExportHistoryAsync()
        {
            try
            {
                // Create and configure the month/year selection window
                var selectionWindow = new Window
                {
                    Title = "Select Month and Year",
                    Width = 300,
                    Height = 250, // Increased from 150 to 250
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.Height // Added this to ensure content fits
                };

                var panel = new StackPanel
                {
                    Margin = new Thickness(20) // Increased margin from 10 to 20
                };

                var yearLabel = new TextBlock { Text = "Year:", Margin = new Thickness(0, 0, 0, 5) };
                var yearComboBox = new ComboBox
                {
                    ItemsSource = AvailableYears,
                    SelectedItem = SelectedYear,
                    Width = 200, // Increased from 100 to 200
                    Margin = new Thickness(0, 0, 0, 15) // Increased bottom margin from 10 to 15
                };


                // Month ComboBox
                var monthLabel = new TextBlock { Text = "Month:", Margin = new Thickness(0, 0, 0, 5) };
                var monthComboBox = new ComboBox
                {
                    Width = 200, // Increased from 100 to 200
                    Margin = new Thickness(0, 0, 0, 20) // Increased bottom margin from 10 to 20
                };


                // Add months
                for (int i = 1; i <= 12; i++)
                {
                    monthComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = new DateTime(2000, i, 1).ToString("MMMM"),
                        Tag = i
                    });
                }
                monthComboBox.SelectedIndex = SelectedMonth - 1;

                var confirmButton = new Button
                {
                    Content = "Export",
                    Width = 200, // Increased from 100 to 200
                    Height = 35, // Increased from 30 to 35
                    Margin = new Thickness(0, 10, 0, 0) // Added margin for better spacing
                };

                bool? dialogResult = null;
                confirmButton.Click += (s, e) =>
                {
                    dialogResult = true;
                    selectionWindow.Close();
                };

                panel.Children.Add(yearLabel);
                panel.Children.Add(yearComboBox);
                panel.Children.Add(monthLabel);
                panel.Children.Add(monthComboBox);
                panel.Children.Add(confirmButton);
                selectionWindow.Content = panel;

                // Show the selection dialog
                selectionWindow.ShowDialog();

                if (dialogResult != true ||
                    yearComboBox.SelectedItem == null ||
                    monthComboBox.SelectedItem == null) return;

                int selectedYear = (int)yearComboBox.SelectedItem;
                int selectedMonth = ((ComboBoxItem)monthComboBox.SelectedItem).Tag is int month ? month : 1;

                var firstDayOfMonth = new DateTime(selectedYear, selectedMonth, 1);
                var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    FileName = $"CustomerHistory_{firstDayOfMonth:yyyyMM}"
                };

                if (saveFileDialog.ShowDialog() != true) return;

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var worksheet = workbook.Worksheets.Add($"History {firstDayOfMonth:MMMM yyyy}");

                // Add headers
                worksheet.Cell(1, 1).Value = "Customer Name";
                worksheet.Cell(1, 2).Value = "Date";
                worksheet.Cell(1, 3).Value = "Previous Reading";
                worksheet.Cell(1, 4).Value = "New Reading";
                worksheet.Cell(1, 5).Value = "Units Used";
                worksheet.Cell(1, 6).Value = "Price/Unit";
                worksheet.Cell(1, 7).Value = "Base Amount";
                worksheet.Cell(1, 8).Value = "Additional Fees";
                worksheet.Cell(1, 9).Value = "Total Bill";

                // Style headers
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                int row = 2;
                using var context = await _contextFactory.CreateDbContextAsync();

                // Get readings within date range including subscription type information
                var monthlyHistory = await context.CounterHistories
                    .Include(ch => ch.CustomerSubscription)
                    .Where(ch => ch.RecordDate.Year == selectedYear &&
                                ch.RecordDate.Month == selectedMonth)
                    .OrderBy(ch => ch.CustomerSubscription.Name)
                    .ThenByDescending(ch => ch.RecordDate)
                    .ToListAsync();

                foreach (var history in monthlyHistory)
                {
                    worksheet.Cell(row, 1).Value = history.CustomerSubscription.Name;
                    worksheet.Cell(row, 2).Value = history.RecordDate;
                    worksheet.Cell(row, 3).Value = history.OldCounter;
                    worksheet.Cell(row, 4).Value = history.NewCounter;
                    worksheet.Cell(row, 5).Value = history.NewCounter - history.OldCounter;
                    worksheet.Cell(row, 6).Value = history.PricePerUnit;
                    worksheet.Cell(row, 7).Value = (history.NewCounter - history.OldCounter) * history.PricePerUnit;
                    worksheet.Cell(row, 8).Value = history.AdditionalFees;
                    worksheet.Cell(row, 9).Value = history.BillAmount;

                    // Format numeric columns
                    worksheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(row, 6).Style.NumberFormat.Format = "$#,##0.00";
                    worksheet.Cell(row, 7).Style.NumberFormat.Format = "$#,##0.00";
                    worksheet.Cell(row, 8).Style.NumberFormat.Format = "$#,##0.00";
                    worksheet.Cell(row, 9).Style.NumberFormat.Format = "$#,##0.00";

                    row++;
                }

                if (row > 2)  // Only add totals if we have data
                {
                    // Add totals row
                    row++;
                    worksheet.Cell(row, 1).Value = "Totals";
                    worksheet.Cell(row, 5).FormulaA1 = $"=SUM(E2:E{row - 1})"; // Total units
                    worksheet.Cell(row, 7).FormulaA1 = $"=SUM(G2:G{row - 1})"; // Total base amount
                    worksheet.Cell(row, 8).FormulaA1 = $"=SUM(H2:H{row - 1})"; // Total additional fees
                    worksheet.Cell(row, 9).FormulaA1 = $"=SUM(I2:I{row - 1})"; // Total bill amount

                    // Style totals row
                    var totalsRow = worksheet.Row(row);
                    totalsRow.Style.Font.Bold = true;
                    totalsRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Save the workbook
                workbook.SaveAs(saveFileDialog.FileName);

                await ShowSuccessMessage($"History exported successfully to {saveFileDialog.FileName}");
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error exporting history: {ex.Message}");
            }
        }


        private async Task DeleteHistoryReadingAsync(CounterHistoryDTO? history)
        {
            if (history == null || SelectedCustomer == null) return;

            try
            {
                // Confirm deletion with the user
                var result = MessageBox.Show(
                    "Are you sure you want to delete this reading? This action cannot be undone.",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                using var context = await _contextFactory.CreateDbContextAsync();
                using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    // Find the history record to delete
                    var historyEntity = await context.CounterHistories
                        .FirstOrDefaultAsync(h => h.Id == history.Id);

                    if (historyEntity == null)
                    {
                        await ShowErrorMessageAsync("History record not found.");
                        return;
                    }

                    // Remove the history record
                    context.CounterHistories.Remove(historyEntity);

                    // If this was the latest reading, we need to update the customer with the next latest reading
                    var latestHistory = await context.CounterHistories
                        .Where(h => h.CustomerSubscriptionId == SelectedCustomer.Id && h.Id != history.Id)
                        .OrderByDescending(h => h.RecordDate)
                        .FirstOrDefaultAsync();

                    if (latestHistory != null)
                    {
                        var customer = await context.CustomerSubscriptions
                            .FirstOrDefaultAsync(c => c.Id == SelectedCustomer.Id);

                        if (customer != null)
                        {
                            // Update customer with the next latest reading
                            customer.OldCounter = latestHistory.OldCounter;
                            customer.NewCounter = latestHistory.NewCounter;
                            customer.BillAmount = latestHistory.BillAmount;
                            customer.LastBillDate = latestHistory.RecordDate;
                            customer.UpdatedAt = DateTime.Now;
                        }
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Refresh the data
                    await LoadCustomerDetailsAsync();
                    await ShowSuccessMessage("Reading deleted successfully");
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error deleting reading: {ex.Message}");
            }
        }
        private async Task PrintReceiptAsync(CounterHistoryDTO? history)
        {
            if (history == null || SelectedCustomer == null) return;

            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        PageHeight = printDialog.PrintableAreaHeight,
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center,
                        Foreground = Brushes.Black,
                        PagePadding = new Thickness(20),
                        ColumnGap = 0
                    };

                    // Title with reduced margins
                    AddCenteredText(flowDocument, "اشتراكات السامريه", 20, true, 0, 0, 0, 3);
                    AddCenteredText(flowDocument, "03657464", 16, false, 0, 0, 0, 3);
                    AddCenteredText(flowDocument, SelectedCustomer.Name, 16, false, 0, 0, 0, 3);

                    // Subscription Type with reduced spacing
                    var subscriptionType = SubscriptionTypes.FirstOrDefault(t => t.Id == SelectedCustomer.SubscriptionTypeId);
                    var counterTable = new Table { CellSpacing = 0 };
                    counterTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                    counterTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

                    var counterRow = new TableRow();
                    AddTableCell(counterRow, subscriptionType?.Name ?? "غير محدد", false);
                    AddTableCell(counterRow, "عداد", true);

                    var counterRowGroup = new TableRowGroup();
                    counterRowGroup.Rows.Add(counterRow);
                    counterTable.RowGroups.Add(counterRowGroup);
                    flowDocument.Blocks.Add(counterTable);

                    // Receipt info with reduced margins
                    var receiptInfo = new Paragraph
                    {
                        TextAlignment = TextAlignment.Right,
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 8),
                        LineHeight = 1
                    };
                    var arabicMonth = GetArabicMonth(history.RecordDate.Month);
                    receiptInfo.Inlines.Add(new Run($"{arabicMonth} - {history.RecordDate.Year} : رقم الوصل - {history.Id:D6}"));
                    flowDocument.Blocks.Add(receiptInfo);

                    // Main Table with reduced spacing
                    var table = new Table
                    {
                        CellSpacing = 0,
                        BorderThickness = new Thickness(1)
                    };

                    for (int i = 0; i < 3; i++)
                        table.Columns.Add(new TableColumn { Width = GridLength.Auto });

                    var tableRowGroup = new TableRowGroup();

                    // Reading Details Row
                    var headerRow = new TableRow();
                    AddTableCell(headerRow, "كمية الإستهلاك", true);
                    AddTableCell(headerRow, "عداد حالي", true);
                    AddTableCell(headerRow, "عداد سابق", true);
                    tableRowGroup.Rows.Add(headerRow);

                    // Values Row
                    decimal consumption = history.NewCounter - history.OldCounter;
                    var valueRow = new TableRow();
                    AddTableCell(valueRow, consumption.ToString("N0"), false);
                    AddTableCell(valueRow, history.NewCounter.ToString("N0"), false);
                    AddTableCell(valueRow, history.OldCounter.ToString("N0"), false);
                    tableRowGroup.Rows.Add(valueRow);

                    // Price Row
                    var priceRow = new TableRow();
                    AddTableCell(priceRow, "سعر الكيلو", true);
                    AddTableCell(priceRow, "رسم الاشتراك", true);
                    AddTableCell(priceRow, "المجموع", true);
                    tableRowGroup.Rows.Add(priceRow);

                    // Price Values Row
                    var priceValueRow = new TableRow();
                    AddTableCell(priceValueRow, history.PricePerUnit.ToString("N2"), false);
                    var subscriptionFee = subscriptionType?.AdditionalCharge ?? 14.00m;
                    AddTableCell(priceValueRow, subscriptionFee.ToString("N2"), false);
                    AddTableCell(priceValueRow, history.BillAmount.ToString("N2"), false);
                    tableRowGroup.Rows.Add(priceValueRow);

                    // Additional Fees Row
                    var additionalFeesRow = new TableRow();
                    var additionalFeesCell = new TableCell(new Paragraph(new Run($"رسوم إضافية: {history.AdditionalFees:N2}"))
                    {
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(5, 2, 5, 2),
                        FontWeight = FontWeights.Bold
                    })
                    {
                        ColumnSpan = 3,
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.Black
                    };
                    additionalFeesRow.Cells.Add(additionalFeesCell);
                    tableRowGroup.Rows.Add(additionalFeesRow);

                    // Total Row
                    var totalRow = new TableRow();
                    var lbpAmount = CurrencyHelper.ConvertToLBP(history.BillAmount);
                    var totalCell = new TableCell(new Paragraph(new Run($"المجموع الكلي: {history.BillAmount:N2} $ || {lbpAmount:N0} L.L"))
                    {
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(5, 2, 5, 2),
                        FontWeight = FontWeights.Bold
                    })
                    {
                        ColumnSpan = 3,
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.Black
                    };
                    totalRow.Cells.Add(totalCell);
                    tableRowGroup.Rows.Add(totalRow);

                    table.RowGroups.Add(tableRowGroup);
                    flowDocument.Blocks.Add(table);

                    // Note with reduced margin
                    AddCenteredText(flowDocument, "ملاحظة: اخر مهلة لدفع الاشتراك 5 الشهر", 14, false, 0, 5, 0, 0);

                    // Print the document
                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        $"Receipt_{SelectedCustomer.Name}_{DateTime.Now:yyyyMMdd}");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing receipt: {ex.Message}");
            }
        }

        private void AddTableCell(TableRow row, string text, bool isHeader)
        {
            var paragraph = new Paragraph(new Run(text))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(2),
                LineHeight = 1
            };

            var cell = new TableCell(paragraph)
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 2, 5, 2)
            };

            row.Cells.Add(cell);
        }
        private async Task PrintAllHistoryAsync()
        {
            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    using var context = await _contextFactory.CreateDbContextAsync();

                    // Get first and last day of current month
                    var today = DateTime.Now;
                    var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
                    var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                    // Get all readings for current month
                    var monthlyReadings = await context.CounterHistories
                        .Include(ch => ch.CustomerSubscription)
                            .ThenInclude(cs => cs.SubscriptionType)
                        .Where(ch => ch.RecordDate >= firstDayOfMonth &&
                                    ch.RecordDate <= lastDayOfMonth)
                        .OrderBy(ch => ch.CustomerSubscription.Name)
                        .ToListAsync();

                    if (!monthlyReadings.Any())
                    {
                        await ShowErrorMessageAsync("No readings found for the current month.");
                        return;
                    }

                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        PageHeight = printDialog.PrintableAreaHeight,
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        TextAlignment = TextAlignment.Right,
                        Foreground = Brushes.Black,
                        PagePadding = new Thickness(50),
                        ColumnGap = 0
                    };

                    // Title
                    AddCenteredText(flowDocument, "تقرير القراءات الشهري", 20, true, 0, 0, 0, 20);
                    AddCenteredText(flowDocument, $"{GetArabicMonth(today.Month)} - {today.Year}", 16, true, 0, 0, 0, 20);

                    // Create main table
                    var table = new Table { CellSpacing = 0 };

                    // Define columns
                    var columns = new[] { "العميل", "عداد سابق", "عداد حالي", "الاستهلاك", "السعر", "الرسوم", "المجموع" };
                    foreach (var _ in columns)
                    {
                        table.Columns.Add(new TableColumn { Width = GridLength.Auto });
                    }

                    // Add header row
                    var headerRow = new TableRow();
                    foreach (var header in columns)
                    {
                        AddTableCell(headerRow, header, true);
                    }

                    var tableRowGroup = new TableRowGroup();
                    tableRowGroup.Rows.Add(headerRow);

                    decimal totalConsumption = 0;
                    decimal totalBills = 0;

                    // Add data rows
                    foreach (var reading in monthlyReadings)
                    {
                        var row = new TableRow();
                        decimal consumption = reading.NewCounter - reading.OldCounter;
                        totalConsumption += consumption;
                        totalBills += reading.BillAmount;

                        AddTableCell(row, reading.CustomerSubscription.Name, false);
                        AddTableCell(row, reading.OldCounter.ToString("N0"), false);
                        AddTableCell(row, reading.NewCounter.ToString("N0"), false);
                        AddTableCell(row, consumption.ToString("N0"), false);
                        AddTableCell(row, reading.PricePerUnit.ToString("N2"), false);
                        AddTableCell(row, reading.AdditionalFees.ToString("N2"), false);
                        AddTableCell(row, reading.BillAmount.ToString("N2"), false);

                        tableRowGroup.Rows.Add(row);
                    }

                    // Add totals row
                    var totalsRow = new TableRow();
                    AddTableCell(totalsRow, "المجموع", true);
                    AddTableCell(totalsRow, "", false);
                    AddTableCell(totalsRow, "", false);
                    AddTableCell(totalsRow, totalConsumption.ToString("N0"), true);
                    AddTableCell(totalsRow, "", false);
                    AddTableCell(totalsRow, "", false);
                    AddTableCell(totalsRow, totalBills.ToString("N2"), true);
                    tableRowGroup.Rows.Add(totalsRow);

                    table.RowGroups.Add(tableRowGroup);
                    flowDocument.Blocks.Add(table);

                    // Add summary section
                    var summary = new Paragraph
                    {
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 20, 0, 0)
                    };
                    summary.Inlines.Add(new Run($"عدد العملاء: {monthlyReadings.Count}\n"));
                    summary.Inlines.Add(new Run($"مجموع الاستهلاك: {totalConsumption:N0} كيلوواط\n"));
                    summary.Inlines.Add(new Run($"مجموع الفواتير: {totalBills:N2} $\n"));
                    summary.Inlines.Add(new Run($"المجموع بالليرة: {CurrencyHelper.ConvertToLBP(totalBills):N0} ل.ل"));
                    flowDocument.Blocks.Add(summary);

                    // Print the document
                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        $"Monthly_Readings_{today:yyyyMM}");

                    await ShowSuccessMessage("Report printed successfully");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing report: {ex.Message}");
            }
        }
        // Add this method to MonthlySubscriptionViewModel
        private async Task ResetPageAsync()
        {
            // Reset selected items
            SelectedCustomer = null;

            // Reset input fields
            NewReading = 0;
            AdditionalFees = 0;
            PaymentAmount = 0;
            PaymentNotes = string.Empty;
            SearchText = string.Empty;

            // Reset calculated values
            BaseCharge = 0;
            TotalBill = 0;
            UnitRate = string.Empty;
            Consumption = 0;

            // Reload all data from database
            await LoadDataAsync();
            await LoadStatisticsAsync();

            // Reset collections
            CounterHistory = new ObservableCollection<CounterHistoryDTO>();
            PaymentHistory = new ObservableCollection<SubscriptionPaymentDTO>();
        }
        private void AddCenteredText(FlowDocument doc, string text, double fontSize, bool isBold, double marginLeft, double marginTop, double marginRight, double marginBottom)
        {
            var paragraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                FontSize = fontSize,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom),
                LineHeight = 1
            };
            paragraph.Inlines.Add(new Run(text));
            doc.Blocks.Add(paragraph);
        }

        private async Task SaveHistoryReadingAsync(CounterHistoryDTO? history)
        {
            if (history == null || SelectedCustomer == null) return;

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    // Use AsNoTracking for the query to avoid tracking conflicts
                    var historyEntity = await context.CounterHistories
                        .AsNoTracking()
                        .FirstOrDefaultAsync(h => h.Id == history.Id);

                    if (historyEntity == null)
                    {
                        await ShowErrorMessageAsync("History record not found.");
                        return;
                    }

                    // Calculate new values
                    decimal unitsUsed = history.NewCounter - history.OldCounter;
                    decimal billAmount = unitsUsed * history.PricePerUnit;

                    // Create a new entity with updated values
                    var updatedHistory = new CounterHistory
                    {
                        Id = history.Id,
                        CustomerSubscriptionId = history.CustomerSubscriptionId,
                        OldCounter = history.OldCounter,
                        NewCounter = history.NewCounter,
                        BillAmount = billAmount,
                        RecordDate = history.RecordDate,
                        PricePerUnit = history.PricePerUnit,
                        AdditionalFees = history.AdditionalFees
                    };

                    // Attach and mark as modified
                    context.CounterHistories.Attach(updatedHistory);
                    context.Entry(updatedHistory).State = EntityState.Modified;

                    // Update customer subscription if it's the latest reading
                    var latestHistory = await context.CounterHistories
                        .AsNoTracking()
                        .Where(h => h.CustomerSubscriptionId == SelectedCustomer.Id)
                        .OrderByDescending(h => h.RecordDate)
                        .FirstOrDefaultAsync();

                    if (latestHistory?.Id == history.Id)
                    {
                        var customer = await context.CustomerSubscriptions
                            .FirstOrDefaultAsync(c => c.Id == SelectedCustomer.Id);

                        if (customer != null)
                        {
                            customer.OldCounter = history.OldCounter;
                            customer.NewCounter = history.NewCounter;
                            customer.BillAmount = billAmount;
                            context.Entry(customer).State = EntityState.Modified;
                        }
                    }

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Refresh the data after successful save
                    await LoadCustomerDetailsAsync();
                    await ShowSuccessMessage("Reading updated successfully");
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error updating reading: {ex.Message}");
            }
        }

        private string GetArabicMonth(int month)
        {
            return month switch
            {
                1 => "كانون الثاني",
                2 => "شباط",
                3 => "آذار",
                4 => "نيسان",
                5 => "أيار",
                6 => "حزيران",
                7 => "تموز",
                8 => "آب",
                9 => "أيلول",
                10 => "تشرين الأول",
                11 => "تشرين الثاني",
                12 => "كانون الأول",
                _ => string.Empty
            };
        }

        private async Task InitializeDataAsync()
        {
            try
            {
                await LoadDataAsync();
                await LoadStatisticsAsync(); // Load statistics after data is loaded
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error initializing data: {ex.Message}");
            }
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Load settings
                _settings = await _settingsService.GetSettingsAsync();
                DefaultUnitPrice = _settings.DefaultUnitPrice;

                // Load subscription types
                var types = await _subscriptionTypeService.GetActiveTypesAsync();
                SubscriptionTypes = new ObservableCollection<SubscriptionTypeDTO>(types);

                // Load all customers
                var allCustomers = await _subscriptionService.GetAllAsync();
                Customers = new ObservableCollection<CustomerSubscriptionDTO>(allCustomers);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading data: {ex.Message}");
            }
        }
        public decimal AdditionalCharge
        {
            get => _additionalCharge;
            set
            {
                if (SetProperty(ref _additionalCharge, value))
                {
                    // Update total bill when additional charge changes
                    if (SelectedCustomer != null)
                    {
                        TotalBill = BaseCharge + value;
                    }
                }
            }
        }
        private async Task SaveSettingsAsync()
        {
            try
            {
                if (DefaultUnitPrice < 0)
                {
                    await ShowErrorMessageAsync("Default unit price cannot be negative.");
                    return;
                }

                // Check if we need to update all customers
                var updateAll = false;
                if (Math.Abs(_settings.DefaultUnitPrice - DefaultUnitPrice) > 0.001m)
                {
                    // Ask for confirmation
                    var result = MessageBox.Show(
                        "Do you want to update the price for all customers?",
                        "Update All Customers",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    updateAll = result == MessageBoxResult.Yes;
                }

                _settings.DefaultUnitPrice = DefaultUnitPrice;

                if (updateAll)
                {
                    await _settingsService.UpdateAllCustomerPricesAsync(DefaultUnitPrice, "Admin");
                    await ShowSuccessMessage("Settings and all customer prices updated successfully.");
                }
                else
                {
                    await _settingsService.UpdateSettingsAsync(_settings);
                    await ShowSuccessMessage("Settings saved successfully.");
                }

                // Reload data to reflect changes
                await LoadDataAsync();
                await ResetPageAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error saving settings: {ex.Message}");
            }
        }
        private void CalculateBill()
        {
            if (SelectedCustomer == null) return;

            var consumption = NewReading - SelectedCustomer.OldCounter;
            if (consumption < 0)
            {
                MessageBox.Show("New reading cannot be less than previous reading",
                    "Invalid Reading", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pricePerUnit = SelectedCustomer.PricePerUnit > 0 ?
                SelectedCustomer.PricePerUnit : DefaultUnitPrice;

            BaseCharge = consumption * pricePerUnit;
            AdditionalCharge = SelectedCustomer.AdditionalCharge + AdditionalFees;
            TotalBill = BaseCharge + AdditionalCharge; // This triggers the setter
            UnitRate = $"{pricePerUnit:C2}/kWh";

            Consumption = consumption;
            OnPropertyChanged(nameof(Consumption));
        }
        private async Task PrintAllReceiptsAsync()
        {
            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    using var context = await _contextFactory.CreateDbContextAsync();

                    // Calculate first and last day of the selected month
                    var firstDayOfMonth = new DateTime(SelectedYear, SelectedMonth, 1);
                    var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                    // Get readings for the selected month using ONLY month and year
                    var monthlyReadings = await context.CounterHistories
                        .Include(ch => ch.CustomerSubscription)
                            .ThenInclude(cs => cs.SubscriptionType)
                        .Where(ch => ch.RecordDate.Year == SelectedYear &&
                                    ch.RecordDate.Month == SelectedMonth)
                        .OrderBy(ch => ch.CustomerSubscription.Name)
                        .ToListAsync();

                    // Display month name in proper format
                    string monthName = new DateTime(2000, SelectedMonth, 1).ToString("MMMM");

                    if (!monthlyReadings.Any())
                    {
                        await ShowErrorMessageAsync($"No readings found for {monthName} {SelectedYear}.");
                        return;
                    }

                    // Create a single FlowDocument for all receipts
                    var flowDocument = new FlowDocument
                    {
                        PageWidth = printDialog.PrintableAreaWidth,
                        PageHeight = 400, // Height for each receipt
                        FontFamily = new FontFamily("Arial"),
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center,
                        Foreground = Brushes.Black,
                        PagePadding = new Thickness(20),
                        ColumnGap = 0,
                        IsColumnWidthFlexible = false,
                        IsOptimalParagraphEnabled = false
                    };

                    foreach (var reading in monthlyReadings)
                    {
                        // Title with reduced margins
                        AddCenteredText(flowDocument, "اشتراكات السامريه", 20, true, 0, 0, 0, 3);
                        AddCenteredText(flowDocument, "03657464", 16, false, 0, 0, 0, 3);
                        AddCenteredText(flowDocument, SelectedCustomer.Name, 16, false, 0, 0, 0, 3);

                        // Subscription Type
                        var counterTable = new Table { CellSpacing = 0 };
                        counterTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                        counterTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

                        var counterRow = new TableRow();
                        AddTableCell(counterRow, reading.CustomerSubscription.SubscriptionType?.Name ?? "غير محدد", false);
                        AddTableCell(counterRow, "عداد", true);

                        var counterRowGroup = new TableRowGroup();
                        counterRowGroup.Rows.Add(counterRow);
                        counterTable.RowGroups.Add(counterRowGroup);
                        flowDocument.Blocks.Add(counterTable);

                        // Receipt info
                        var receiptInfo = new Paragraph
                        {
                            TextAlignment = TextAlignment.Right,
                            FontSize = 13,
                            Margin = new Thickness(0, 0, 0, 8),
                            LineHeight = 1
                        };
                        var arabicMonth = GetArabicMonth(reading.RecordDate.Month);
                        receiptInfo.Inlines.Add(new Run($"{arabicMonth} - {reading.RecordDate.Year} : رقم الوصل - {reading.Id:D6}"));
                        flowDocument.Blocks.Add(receiptInfo);

                        // Main Table
                        var table = new Table { CellSpacing = 0 };
                        for (int i = 0; i < 3; i++)
                            table.Columns.Add(new TableColumn { Width = GridLength.Auto });

                        var tableRowGroup = new TableRowGroup();

                        // Reading Details
                        var headerRow = new TableRow();
                        AddTableCell(headerRow, "كمية الإستهلاك", true);
                        AddTableCell(headerRow, "عداد حالي", true);
                        AddTableCell(headerRow, "عداد سابق", true);
                        tableRowGroup.Rows.Add(headerRow);

                        decimal consumption = reading.NewCounter - reading.OldCounter;
                        var valueRow = new TableRow();
                        AddTableCell(valueRow, consumption.ToString("N0"), false);
                        AddTableCell(valueRow, reading.NewCounter.ToString("N0"), false);
                        AddTableCell(valueRow, reading.OldCounter.ToString("N0"), false);
                        tableRowGroup.Rows.Add(valueRow);

                        var priceRow = new TableRow();
                        AddTableCell(priceRow, "سعر الكيلو", true);
                        AddTableCell(priceRow, "رسم الاشتراك", true);
                        AddTableCell(priceRow, "المجموع", true);
                        tableRowGroup.Rows.Add(priceRow);

                        var subscriptionFee = reading.CustomerSubscription.SubscriptionType?.AdditionalCharge ?? 14.00m;
                        var priceValueRow = new TableRow();
                        AddTableCell(priceValueRow, reading.PricePerUnit.ToString("N2"), false);
                        AddTableCell(priceValueRow, subscriptionFee.ToString("N2"), false);
                        AddTableCell(priceValueRow, reading.BillAmount.ToString("N2"), false);
                        tableRowGroup.Rows.Add(priceValueRow);

                        table.RowGroups.Add(tableRowGroup);
                        flowDocument.Blocks.Add(table);

                        // Additional Fees
                        var feesBlock = new Paragraph(new Run($"رسوم إضافية: {reading.AdditionalFees:N2}"))
                        {
                            TextAlignment = TextAlignment.Right,
                            Margin = new Thickness(0, 8, 0, 8)
                        };
                        flowDocument.Blocks.Add(feesBlock);

                        // Total
                        var lbpAmount = CurrencyHelper.ConvertToLBP(reading.BillAmount);
                        var totalBlock = new Paragraph(new Run($"المجموع الكلي: {reading.BillAmount:N2} $ || {lbpAmount:N0} L.L"))
                        {
                            TextAlignment = TextAlignment.Right,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 8, 0, 8)
                        };
                        flowDocument.Blocks.Add(totalBlock);

                        // Note
                        AddCenteredText(flowDocument, "ملاحظة: اخر مهلة لدفع الاشتراك 5 الشهر", 14, false, 0, 5, 0, 0);

                        // Add page break for receipt cutter
                        flowDocument.Blocks.Add(new BlockUIContainer(new Rectangle
                        {
                            Height = 50,
                            Fill = Brushes.White
                        }));
                    }

                    // Print the document
                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)flowDocument).DocumentPaginator,
                        $"All_Receipts_{SelectedYear}_{SelectedMonth}");

                    await ShowSuccessMessage($"All receipts for {monthName} {SelectedYear} printed successfully");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing receipts: {ex.Message}");
            }
        }
        private async Task SaveReadingAsync()
        {
            try
            {
                if (SelectedCustomer == null)
                {
                    await ShowErrorMessageAsync("Please select a customer.");
                    return;
                }

                // Validate new reading against previous reading
                if (NewReading < SelectedCustomer.OldCounter)
                {
                    var result = MessageBox.Show(
                        "The new reading appears to be less than the previous reading. " +
                        "Is this due to a meter rollover?",
                        "Confirm Meter Rollover",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        await ShowErrorMessageAsync("New reading cannot be less than previous reading.");
                        return;
                    }
                }

                // Ensure bill is calculated
                if (TotalBill <= 0)
                {
                    await ShowErrorMessageAsync("Please calculate the bill before saving.");
                    return;
                }

                var counterHistory = new CounterHistoryDTO
                {
                    CustomerSubscriptionId = SelectedCustomer.Id,
                    OldCounter = SelectedCustomer.OldCounter,
                    NewCounter = NewReading,
                    BillAmount = TotalBill,
                    RecordDate = MeterReadingDate, // Changed from DateTime.Now
                    PricePerUnit = SelectedCustomer.PricePerUnit > 0 ?
                     SelectedCustomer.PricePerUnit :
                     DefaultUnitPrice,
                    UnitsUsed = Math.Abs(NewReading - SelectedCustomer.OldCounter),
                    AdditionalFees = AdditionalFees
                };

                // Create a new context and transaction scope just for this operation
                using (var context = await _contextFactory.CreateDbContextAsync())
                {
                    // Start transaction
                    using (var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            // Fetch the actual customer entity
                            var customer = await context.CustomerSubscriptions
                                .Include(c => c.SubscriptionType)
                                .FirstOrDefaultAsync(c => c.Id == SelectedCustomer.Id);

                            if (customer == null)
                            {
                                await ShowErrorMessageAsync("Customer not found.");
                                return;
                            }

                            // Update customer fields
                            customer.OldCounter = SelectedCustomer.OldCounter;
                            customer.NewCounter = NewReading;
                            customer.LastBillDate = MeterReadingDate; // Changed from DateTime.Now
                            customer.BillAmount = TotalBill;
                            customer.UpdatedAt = DateTime.Now;

                            // Map and add counter history
                            var counterHistoryEntity = _mapper.Map<CounterHistory>(counterHistory);
                            await context.CounterHistories.AddAsync(counterHistoryEntity);

                            // Save changes inside the transaction
                            await context.SaveChangesAsync();

                            // Commit the transaction
                            await transaction.CommitAsync();

                            // Only publish events after successful commit
                            _eventAggregator.Publish(new MeterReadingUpdatedEvent(
                                SelectedCustomer.Id,
                                NewReading,
                                MeterReadingDate // Changed from DateTime.Now
                               ));

                            // Show success and reset UI after commit
                            await ShowSuccessMessage("Reading saved successfully");
                            await ResetPageAsync();
                        }
                        catch (Exception innerEx)
                        {
                            // Make sure to rollback the transaction if there's an error
                            await transaction.RollbackAsync();
                            Debug.WriteLine($"Inner transaction error: {innerEx}");
                            throw; // Rethrow to be caught by outer catch
                        }
                    } // Transaction is properly disposed here
                } // Context is properly disposed here
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Outer save reading error: {ex}");
                await ShowErrorMessageAsync($"Error saving reading: {ex.Message}");
            }
        }

        // Extract reset logic to a separate method for clarity
        private void ResetInputFields()
        {
            // Keep SelectedCustomer for now to maintain context
            NewReading = 0;
            AdditionalFees = 0;
            BaseCharge = 0;
            TotalBill = 0;
            UnitRate = string.Empty;
            Consumption = 0;
        }
        private async Task AddSubscriptionTypeAsync()
        {
            try
            {
                var dialog = new InputDialog("New Subscription Type", "Enter subscription type name:");
                if (dialog.ShowDialog() == true)
                {
                    var type = new SubscriptionTypeDTO
                    {
                        Name = dialog.Input,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };

                    var chargeDialog = new InputDialog("Additional Charge", "Enter additional charge amount:");
                    if (chargeDialog.ShowDialog() == true && decimal.TryParse(chargeDialog.Input, out decimal charge))
                    {
                        type.AdditionalCharge = charge;
                        await _subscriptionTypeService.CreateAsync(type);
                        await LoadDataAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error adding subscription type: {ex.Message}");
            }
        }
        // Remove the override keyword
        private async Task LoadCustomerDetailsAsync()
        {
            if (SelectedCustomer == null) return;

            try
            {
                var history = await _subscriptionService.GetCustomerHistoryAsync(SelectedCustomer.Id);
                CounterHistory = new ObservableCollection<CounterHistoryDTO>(
                    history.OrderByDescending(h => h.RecordDate));  // Sort in memory

                // Get the latest reading
                var latestReading = CounterHistory.FirstOrDefault();
                if (latestReading != null)
                {
                    SelectedCustomer.OldCounter = latestReading.NewCounter;
                    SelectedCustomer.BillAmount = latestReading.BillAmount;
                    SelectedCustomer.NewCounter = latestReading.NewCounter;
                    SelectedCustomer.LastBillDate = latestReading.RecordDate;

                    var subscriptionType = SubscriptionTypes
                        .FirstOrDefault(t => t.Id == SelectedCustomer.SubscriptionTypeId);

                    if (subscriptionType != null)
                    {
                        SelectedCustomer.AdditionalCharge = subscriptionType.AdditionalCharge;
                    }

                    OnPropertyChanged(nameof(SelectedCustomer));
                }

                // Reset selected date to current month and load payment details
                SelectedBillingDate = DateTime.Now;
                await LoadPaymentDetailsAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading customer details: {ex.Message}");
            }
        }

        private void AddNew()
        {
            // Initialize new customer object
            NewCustomer = new CustomerSubscriptionDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now,
                LastBillDate = DateTime.Now,
                PricePerUnit = DefaultUnitPrice
            };

            // Check if a "No Fees" subscription type already exists
            var noFeesType = SubscriptionTypes.FirstOrDefault(t => t.Name == "No Fees" && t.AdditionalCharge == 0);

            // If not, create a temporary one for the dialog
            if (noFeesType == null)
            {
                // Check if we need to initialize the collection
                if (SubscriptionTypes == null)
                    SubscriptionTypes = new ObservableCollection<SubscriptionTypeDTO>();

                // Add a "No Fees" subscription type
                SubscriptionTypes.Insert(0, new SubscriptionTypeDTO
                {
                    Id = -1, // Temporary ID
                    Name = "No Fees",
                    AdditionalCharge = 0,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            // Show the dialog
            IsNewCustomerDialogOpen = true;
        }
        private async Task SaveAsync()
        {
            try
            {
                if (SelectedCustomer == null)
                {
                    await ShowErrorMessageAsync("No customer selected.");
                    return;
                }

                if (!SelectedCustomer.IsValid(out string errorMessage))
                {
                    await ShowErrorMessageAsync(errorMessage);
                    return;
                }

                if (SelectedCustomer.Id == 0)
                {
                    // For new customers
                    await _subscriptionService.CreateAsync(SelectedCustomer);
                    await ShowSuccessMessage("Customer created successfully.");
                }
                else
                {
                    // For existing customers
                    await _subscriptionService.UpdateAsync(SelectedCustomer);
                    await ShowSuccessMessage("Customer updated successfully.");
                }
                await ResetPageAsync();

                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error saving customer: {ex.Message}");
            }
        }


        private async Task DeleteAsync()
        {
            try
            {
                if (SelectedCustomer == null) return;

                if (MessageBox.Show("Are you sure you want to delete this customer?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _subscriptionService.DeleteAsync(SelectedCustomer.Id);
                    await LoadDataAsync();
                }
                await ResetPageAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error deleting customer: {ex.Message}");
            }
        }
        private async Task SubmitNewCustomerAsync()
        {
            try
            {
                if (NewCustomer == null)
                {
                    await ShowErrorMessageAsync("No customer data provided.");
                    return;
                }

                if (!NewCustomer.IsValid(out string errorMessage))
                {
                    await ShowErrorMessageAsync(errorMessage);
                    return;
                }

                // Check if the temporary "No Fees" type was selected
                if (NewCustomer.SubscriptionTypeId == -1)
                {
                    // Create a real "No Fees" subscription type in the database
                    var noFeesType = new SubscriptionTypeDTO
                    {
                        Name = "No Fees",
                        Description = "Default type with no additional charges",
                        AdditionalCharge = 0,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };

                    var createdType = await _subscriptionTypeService.CreateAsync(noFeesType);

                    // Update the subscription type ID
                    NewCustomer.SubscriptionTypeId = createdType.Id;

                    // Remove the temporary type from the collection
                    var tempType = SubscriptionTypes.FirstOrDefault(t => t.Id == -1);
                    if (tempType != null)
                        SubscriptionTypes.Remove(tempType);
                }

                await _subscriptionService.CreateAsync(NewCustomer);
                await ShowSuccessMessage("Customer created successfully.");

                // Reset and close dialog
                IsNewCustomerDialogOpen = false;
                NewCustomer = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error creating customer: {ex.Message}");
            }
        }
        private async Task UpdateCounterAsync()
        {
            try
            {
                if (SelectedCustomer == null) return;

                var dialog = new InputDialog("Update Counter", "Enter new counter value:");
                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal newCounter))
                {
                    var pricePerUnit = SelectedCustomer.PricePerUnit > 0 ?
                        SelectedCustomer.PricePerUnit : DefaultUnitPrice;

                    var unitsUsed = newCounter - SelectedCustomer.OldCounter;
                    var baseCharge = unitsUsed * pricePerUnit;
                    var totalCharge = baseCharge + SelectedCustomer.AdditionalCharge;

                    await _subscriptionService.UpdateCounterAsync(SelectedCustomer.Id, newCounter);
                    await LoadDataAsync();
                    await LoadCustomerDetailsAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error updating counter: {ex.Message}");
            }
        }
        private void CancelNewCustomer()
        {
            IsNewCustomerDialogOpen = false;
            NewCustomer = null;
        }
        private async Task ProcessPaymentAsync()
        {
            try
            {
                if (SelectedCustomer == null)
                {
                    await ShowErrorMessageAsync("Please select a customer.");
                    return;
                }

                if (PaymentAmount <= 0)
                {
                    await ShowErrorMessageAsync("Please enter a valid payment amount.");
                    return;
                }

                var payment = new SubscriptionPaymentDTO
                {
                    CustomerSubscriptionId = SelectedCustomer.Id,
                    Amount = PaymentAmount,
                    PaymentDate = DateTime.Now,
                    Notes = PaymentNotes
                };

                if (await _subscriptionService.ProcessPaymentAsync(payment))
                {
                    await LoadCustomerDetailsAsync();
                    PaymentAmount = 0;
                    PaymentNotes = string.Empty;
                    await ShowSuccessMessage("Payment processed successfully.");
                }
                await ResetPageAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error processing payment: {ex.Message}");
            }
        }
        private async Task SearchCustomersAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    await LoadDataAsync();
                    return;
                }

                // Use GetAllAsync and filter in memory, or implement a new service method if needed
                var allCustomers = await _subscriptionService.GetAllAsync();
                var filteredCustomers = allCustomers.Where(c =>
                    c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
                Customers = new ObservableCollection<CustomerSubscriptionDTO>(filteredCustomers);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error searching customers: {ex.Message}");
            }
        }
    }
}