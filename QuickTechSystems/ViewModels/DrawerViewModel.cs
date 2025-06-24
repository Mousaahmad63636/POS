// QuickTechSystems/ViewModels/DrawerViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Services;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.ViewModels
{
    public class DrawerViewModel : ViewModelBase
    {
        #region Private Fields

        private readonly IDrawerService _drawerService;
        private readonly IWindowService _windowService;
        private readonly IBusinessSettingsService _businessSettingsService;

        // Core Properties
        private DrawerDTO? _currentDrawer;
        private ObservableCollection<DrawerTransactionDTO> _drawerHistory = new();
        private string _statusMessage = string.Empty;
        private bool _isProcessing;

        // Financial Data
        private FinancialTotals _financialTotals = new();

        // Thread safety
        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);
        private bool _isInitialized = false;

        // Session Management
        private ObservableCollection<DrawerSessionItem> _drawerSessions = new();
        private DrawerSessionItem? _selectedDrawerSession;
        private bool _isViewingHistoricalSession;
        private DateTime _sessionStartDate = DateTime.Today.AddDays(-30);
        private DateTime _sessionEndDate = DateTime.Today;

        // Date Filtering
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;

        // UI Operations
        private decimal _cashAmount;
        private string _cashDescription = string.Empty;
        private decimal _finalCashAmount;
        private decimal _initialCashAmount;

        // Report Options
        private bool _includeTransactionDetails = true;
        private bool _includeFinancialSummary = true;
        private bool _printCashierCopy = false;

        // Child ViewModels (placeholders)
        private object? _transactionHistoryViewModel;
        private object? _profitViewModel;

        #endregion

        #region Constructor

        public DrawerViewModel(
            IDrawerService drawerService,
            IWindowService windowService,
            IBusinessSettingsService businessSettingsService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _drawerService = drawerService;
            _windowService = windowService;
            _businessSettingsService = businessSettingsService;

            // Initialize child ViewModels as placeholders
            // These can be injected later if needed
            TransactionHistoryViewModel = null;
            ProfitViewModel = null;

            // Initialize commands
            InitializeCommands();

            // Load initial data
            _ = LoadInitialDataAsync();
        }

        #endregion

        #region Public Properties

        public DrawerDTO? CurrentDrawer
        {
            get => _currentDrawer;
            set
            {
                if (SetProperty(ref _currentDrawer, value))
                {
                    OnPropertyChanged(nameof(IsDrawerOpen));
                    OnPropertyChanged(nameof(CanOpenDrawer));
                    OnPropertyChanged(nameof(CurrentBalance));
                    OnPropertyChanged(nameof(ExpectedBalance));
                    OnPropertyChanged(nameof(Difference));
                }
            }
        }

        public ObservableCollection<DrawerTransactionDTO> DrawerHistory
        {
            get => _drawerHistory;
            set => SetProperty(ref _drawerHistory, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<DrawerSessionItem> DrawerSessions
        {
            get => _drawerSessions;
            set => SetProperty(ref _drawerSessions, value);
        }

        public DrawerSessionItem? SelectedDrawerSession
        {
            get => _selectedDrawerSession;
            set
            {
                if (SetProperty(ref _selectedDrawerSession, value))
                {
                    _ = LoadSelectedSessionAsync();
                }
            }
        }

        public bool IsViewingHistoricalSession
        {
            get => _isViewingHistoricalSession;
            set => SetProperty(ref _isViewingHistoricalSession, value);
        }

        public decimal CashAmount
        {
            get => _cashAmount;
            set => SetProperty(ref _cashAmount, value);
        }

        public string CashDescription
        {
            get => _cashDescription;
            set => SetProperty(ref _cashDescription, value);
        }

        public decimal FinalCashAmount
        {
            get => _finalCashAmount;
            set
            {
                if (SetProperty(ref _finalCashAmount, value))
                {
                    OnPropertyChanged(nameof(DrawerClosingDifference));
                }
            }
        }

        public decimal InitialCashAmount
        {
            get => _initialCashAmount;
            set => SetProperty(ref _initialCashAmount, value);
        }

        public bool IncludeTransactionDetails
        {
            get => _includeTransactionDetails;
            set => SetProperty(ref _includeTransactionDetails, value);
        }

        public bool IncludeFinancialSummary
        {
            get => _includeFinancialSummary;
            set => SetProperty(ref _includeFinancialSummary, value);
        }

        public bool PrintCashierCopy
        {
            get => _printCashierCopy;
            set => SetProperty(ref _printCashierCopy, value);
        }

        public DateTime SessionStartDate
        {
            get => _sessionStartDate;
            set => SetProperty(ref _sessionStartDate, value);
        }

        public DateTime SessionEndDate
        {
            get => _sessionEndDate;
            set => SetProperty(ref _sessionEndDate, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    if (EndDate < value)
                    {
                        EndDate = value;
                    }
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        public object? TransactionHistoryViewModel
        {
            get => _transactionHistoryViewModel;
            set => SetProperty(ref _transactionHistoryViewModel, value);
        }

        public object? ProfitViewModel
        {
            get => _profitViewModel;
            set => SetProperty(ref _profitViewModel, value);
        }

        #endregion

        #region Computed Properties

        public bool IsDrawerOpen => CurrentDrawer?.Status == "Open";
        public bool CanOpenDrawer => CurrentDrawer == null || CurrentDrawer.Status == "Closed";
        public decimal CurrentBalance => CurrentDrawer?.CurrentBalance ?? 0;
        public decimal ExpectedBalance => CurrentDrawer?.ExpectedBalance ?? 0;
        public decimal Difference => CurrentDrawer?.Difference ?? 0;
        public decimal DrawerClosingDifference => CurrentDrawer != null ? FinalCashAmount - CurrentDrawer.CurrentBalance : 0;

        // Financial Properties (using standardized calculations)
        public decimal TotalSales => _financialTotals.TotalSales;
        public decimal TotalExpenses => _financialTotals.TotalExpenses;
        public decimal TotalSupplierPayments => _financialTotals.TotalSupplierPayments;
        public decimal TotalReturns => _financialTotals.TotalReturns;
        public decimal NetSales => _financialTotals.NetSales;
        public decimal NetCashflow => _financialTotals.NetCashFlow;
        public decimal TotalCashIn => _financialTotals.TotalCashIn;
        public decimal TotalCashOut => _financialTotals.TotalCashOut;

        #endregion

        #region Commands

        public ICommand OpenDrawerCommand { get; private set; }
        public ICommand AddCashCommand { get; private set; }
        public ICommand RemoveCashCommand { get; private set; }
        public ICommand CloseDrawerCommand { get; private set; }
        public ICommand LoadDrawerSessionsCommand { get; private set; }
        public ICommand ViewCurrentSessionCommand { get; private set; }
        public ICommand LoadFinancialDataCommand { get; private set; }
        public ICommand PrintReportCommand { get; private set; }
        public ICommand ApplyDateFilterCommand { get; private set; }
        public ICommand ApplySessionFilterCommand { get; private set; }

        private void InitializeCommands()
        {
            OpenDrawerCommand = new AsyncRelayCommand(async _ => await OpenDrawerAsync(), _ => CanOpenDrawer && !IsProcessing);
            AddCashCommand = new AsyncRelayCommand(async _ => await AddCashAsync(), _ => IsDrawerOpen && !IsProcessing);
            RemoveCashCommand = new AsyncRelayCommand(async _ => await RemoveCashAsync(), _ => IsDrawerOpen && !IsProcessing);
            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync(), _ => IsDrawerOpen && !IsProcessing);
            LoadDrawerSessionsCommand = new AsyncRelayCommand(async _ => await LoadDrawerSessionsAsync());
            ViewCurrentSessionCommand = new AsyncRelayCommand(async _ => await ViewCurrentSessionAsync());
            LoadFinancialDataCommand = new AsyncRelayCommand(async _ => await LoadFinancialOverviewAsync());
            PrintReportCommand = new AsyncRelayCommand(async _ => await PrintReportAsync(), _ => IsDrawerOpen && !IsProcessing);
            ApplyDateFilterCommand = new AsyncRelayCommand(async _ => await ApplyDateFilterAsync());
            ApplySessionFilterCommand = new AsyncRelayCommand(async _ => await ApplySessionFilterAsync());
        }

        #endregion

        #region Data Loading

        private async Task LoadInitialDataAsync()
        {
            try
            {
                Debug.WriteLine("Loading initial drawer data");
                IsProcessing = true;

                await LoadCurrentDrawerAsync();
                await LoadDrawerSessionsAsync();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading initial drawer data: {ex.Message}");
                await ShowErrorMessageAsync("Error loading drawer data. Please try again.");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task LoadCurrentDrawerAsync()
        {
            try
            {
                CurrentDrawer = await _drawerService.GetCurrentDrawerAsync();
                if (CurrentDrawer != null)
                {
                    await LoadDrawerHistoryAsync();
                    CalculateFinancialTotals();
                    IsViewingHistoricalSession = false;
                }
                else
                {
                    DrawerHistory.Clear();
                    ResetFinancialTotals();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading current drawer: {ex.Message}");
                throw;
            }
        }

        private async Task LoadDrawerHistoryAsync()
        {
            if (CurrentDrawer == null) return;

            try
            {
                Debug.WriteLine($"Loading history for drawer {CurrentDrawer.DrawerId}");

                var history = await _drawerService.GetDrawerHistoryAsync(CurrentDrawer.DrawerId);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DrawerHistory = new ObservableCollection<DrawerTransactionDTO>(
                        history.OrderByDescending(t => t.Timestamp)
                    );
                });

                CalculateFinancialTotals();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading drawer history for drawer {CurrentDrawer.DrawerId}: {ex.Message}");
                await ShowErrorMessageAsync("Unable to load drawer history. Please try refreshing.");
                DrawerHistory = new ObservableCollection<DrawerTransactionDTO>();
                ResetFinancialTotals();
            }
        }

        private async Task LoadDrawerSessionsAsync()
        {
            try
            {
                IsProcessing = true;
                Debug.WriteLine("Loading drawer sessions");

                var sessions = await _drawerService.GetAllDrawerSessionsAsync(
                    SessionStartDate,
                    SessionEndDate
                );

                var sessionItems = sessions.Select(s => new DrawerSessionItem
                {
                    DrawerId = s.DrawerId,
                    OpenedAt = s.OpenedAt,
                    ClosedAt = s.ClosedAt,
                    CashierName = s.CashierName,
                    Status = s.Status
                }).ToList();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DrawerSessions = new ObservableCollection<DrawerSessionItem>(sessionItems);

                    // Auto-select current session if it exists
                    var currentSession = sessionItems.FirstOrDefault(s => s.Status == "Open");
                    if (currentSession != null)
                    {
                        SelectedDrawerSession = currentSession;
                        IsViewingHistoricalSession = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading drawer sessions: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading drawer sessions: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        #endregion

        #region Session Management

        private async Task LoadSelectedSessionAsync()
        {
            if (SelectedDrawerSession == null) return;

            try
            {
                IsProcessing = true;
                Debug.WriteLine($"Loading selected session: {SelectedDrawerSession.DrawerId}");

                var drawer = await _drawerService.GetDrawerSessionByIdAsync(SelectedDrawerSession.DrawerId);
                if (drawer != null)
                {
                    CurrentDrawer = drawer;
                    IsViewingHistoricalSession = drawer.Status != "Open";
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading selected session: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading selected session: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ViewCurrentSessionAsync()
        {
            try
            {
                IsProcessing = true;
                Debug.WriteLine("Loading current session");

                CurrentDrawer = await _drawerService.GetCurrentDrawerAsync();
                IsViewingHistoricalSession = false;

                if (CurrentDrawer != null)
                {
                    var currentSessionItem = DrawerSessions?.FirstOrDefault(s => s.DrawerId == CurrentDrawer.DrawerId);
                    if (currentSessionItem != null)
                    {
                        SelectedDrawerSession = currentSessionItem;
                    }
                }

                await LoadDrawerHistoryAsync();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading current session: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading current session: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ApplySessionFilterAsync()
        {
            try
            {
                if (SessionStartDate > SessionEndDate)
                {
                    await ShowErrorMessageAsync("Start date cannot be after end date");
                    return;
                }

                await LoadDrawerSessionsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying session filter: {ex.Message}");
                await ShowErrorMessageAsync($"Error applying session filter: {ex.Message}");
            }
        }

        #endregion

        #region Date Filtering

        private async Task ApplyDateFilterAsync()
        {
            try
            {
                IsProcessing = true;
                Debug.WriteLine($"Applying date filter: {StartDate:d} to {EndDate:d}");

                if (StartDate > EndDate)
                {
                    await ShowErrorMessageAsync("Start date cannot be after end date");
                    return;
                }

                // Load filtered transactions
                if (CurrentDrawer != null)
                {
                    var history = await _drawerService.GetDrawerHistoryAsync(CurrentDrawer.DrawerId);
                    var filteredHistory = history.Where(t =>
                        t.Timestamp.Date >= StartDate.Date &&
                        t.Timestamp.Date <= EndDate.Date)
                        .OrderByDescending(t => t.Timestamp);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DrawerHistory = new ObservableCollection<DrawerTransactionDTO>(filteredHistory);
                    });

                    // Recalculate financial totals based on filtered data
                    var dateRange = EndDate.Date == StartDate.Date ? StartDate.Date : (DateTime?)null;
                    _financialTotals = TransactionCalculator.CalculateFinancialTotals(filteredHistory, dateRange);

                    // Update the UI
                    UpdateStatus();
                    NotifyFinancialPropertiesChanged();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying date filter: {ex.Message}");
                await ShowErrorMessageAsync($"Error applying date filter: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        #endregion

        #region Financial Calculations

        private void CalculateFinancialTotals()
        {
            try
            {
                if (CurrentDrawer == null || !DrawerHistory.Any())
                {
                    ResetFinancialTotals();
                    return;
                }

                Debug.WriteLine("Calculating financial totals");

                // Use standardized calculator for all financial calculations
                var todayTransactions = DrawerHistory.Where(t => t.Timestamp.Date == DateTime.Today);
                _financialTotals = TransactionCalculator.CalculateFinancialTotals(todayTransactions, DateTime.Today);

                // Notify UI of changes
                NotifyFinancialPropertiesChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating financial totals: {ex.Message}");
                ResetFinancialTotals();
            }
        }

        private void ResetFinancialTotals()
        {
            _financialTotals = new FinancialTotals();
            NotifyFinancialPropertiesChanged();
        }

        private void NotifyFinancialPropertiesChanged()
        {
            OnPropertyChanged(nameof(TotalSales));
            OnPropertyChanged(nameof(TotalReturns));
            OnPropertyChanged(nameof(TotalExpenses));
            OnPropertyChanged(nameof(TotalSupplierPayments));
            OnPropertyChanged(nameof(NetSales));
            OnPropertyChanged(nameof(NetCashflow));
            OnPropertyChanged(nameof(TotalCashIn));
            OnPropertyChanged(nameof(TotalCashOut));
            OnPropertyChanged(nameof(CurrentBalance));
            OnPropertyChanged(nameof(ExpectedBalance));
            OnPropertyChanged(nameof(Difference));
        }

        private async Task LoadFinancialOverviewAsync()
        {
            // This method is for command binding compatibility
            CalculateFinancialTotals();
            await Task.CompletedTask;
        }

        #endregion

        #region Drawer Operations

        private async Task OpenDrawerAsync()
        {
            try
            {
                var currentUser = System.Windows.Application.Current.Properties["CurrentUser"] as EmployeeDTO;
                if (currentUser == null)
                {
                    await ShowErrorMessageAsync("No user is currently logged in");
                    return;
                }

                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Open Drawer", "Enter opening balance:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    if (amount < 0)
                    {
                        await ShowErrorMessageAsync("Amount cannot be negative");
                        return;
                    }

                    CurrentDrawer = await _drawerService.OpenDrawerAsync(
                        amount,
                        currentUser.EmployeeId.ToString(),
                        $"{currentUser.FirstName} {currentUser.LastName}"
                    );

                    await LoadDrawerHistoryAsync();
                    await LoadDrawerSessionsAsync(); // Refresh sessions
                    UpdateStatus();

                    MessageBox.Show("Drawer opened successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening drawer: {ex.Message}");
                await ShowErrorMessageAsync($"Error opening drawer: {ex.Message}");
            }
        }

        private async Task AddCashAsync()
        {
            try
            {
                IsProcessing = true;

                if (CashAmount <= 0)
                {
                    await ShowErrorMessageAsync("Amount must be greater than zero");
                    return;
                }

                CurrentDrawer = await _drawerService.ProcessTransactionAsync(
                    CashAmount,
                    TransactionCalculator.TransactionTypes.CashIn,
                    string.IsNullOrWhiteSpace(CashDescription) ? "Cash added to drawer" : CashDescription
                );

                await LoadDrawerHistoryAsync();
                UpdateStatus();

                MessageBox.Show($"Successfully added {CashAmount:C2}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset form
                CashAmount = 0;
                CashDescription = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding cash: {ex.Message}");
                await ShowErrorMessageAsync($"Error adding cash: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task RemoveCashAsync()
        {
            try
            {
                IsProcessing = true;

                if (CashAmount <= 0)
                {
                    await ShowErrorMessageAsync("Amount must be greater than zero");
                    return;
                }

                if (CashAmount > CurrentDrawer?.CurrentBalance)
                {
                    await ShowErrorMessageAsync("Amount exceeds current balance");
                    return;
                }

                CurrentDrawer = await _drawerService.ProcessTransactionAsync(
                    CashAmount,
                    TransactionCalculator.TransactionTypes.CashOut,
                    string.IsNullOrWhiteSpace(CashDescription) ? "Cash removed from drawer" : CashDescription
                );

                await LoadDrawerHistoryAsync();
                UpdateStatus();

                MessageBox.Show($"Successfully removed {CashAmount:C2}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset form
                CashAmount = 0;
                CashDescription = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing cash: {ex.Message}");
                await ShowErrorMessageAsync($"Error removing cash: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task CloseDrawerAsync()
        {
            try
            {
                IsProcessing = true;

                if (FinalCashAmount < 0)
                {
                    await ShowErrorMessageAsync("Final amount cannot be negative");
                    return;
                }

                decimal currentBalance = CurrentDrawer?.CurrentBalance ?? 0;
                CurrentDrawer = await _drawerService.CloseDrawerAsync(FinalCashAmount, string.Empty);

                await LoadDrawerHistoryAsync();
                await LoadDrawerSessionsAsync(); // Refresh sessions
                UpdateStatus();

                decimal difference = FinalCashAmount - currentBalance;

                var message = difference == 0
                    ? "Drawer closed successfully with no discrepancy."
                    : $"Drawer closed with a {(difference > 0 ? "surplus" : "shortage")} of {Math.Abs(difference):C2}";

                MessageBox.Show(message, "Drawer Closed",
                    MessageBoxButton.OK,
                    difference == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // Reset form
                FinalCashAmount = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing drawer: {ex.Message}");
                await ShowErrorMessageAsync($"Error closing drawer: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task PrintReportAsync()
        {
            // Placeholder for print functionality
            await ShowErrorMessageAsync("Print functionality will be implemented based on your requirements.");
        }

        #endregion

        #region Public Methods (for View binding)

        public async Task OpenDrawerWithAmount(decimal amount)
        {
            try
            {
                var currentUser = System.Windows.Application.Current.Properties["CurrentUser"] as EmployeeDTO;
                if (currentUser == null)
                {
                    await ShowErrorMessageAsync("No user is currently logged in");
                    return;
                }

                if (amount < 0)
                {
                    await ShowErrorMessageAsync("Amount cannot be negative");
                    return;
                }

                CurrentDrawer = await _drawerService.OpenDrawerAsync(
                    amount,
                    currentUser.EmployeeId.ToString(),
                    $"{currentUser.FirstName} {currentUser.LastName}"
                );

                await LoadDrawerHistoryAsync();
                await LoadDrawerSessionsAsync();
                UpdateStatus();

                MessageBox.Show("Drawer opened successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening drawer: {ex.Message}");
                await ShowErrorMessageAsync($"Error opening drawer: {ex.Message}");
            }
        }

        public async Task AddCashWithDetails(decimal amount, string description)
        {
            CashAmount = amount;
            CashDescription = description;
            await AddCashAsync();
        }

        public async Task RemoveCashWithDetails(decimal amount, string description)
        {
            CashAmount = amount;
            CashDescription = description;
            await RemoveCashAsync();
        }

        public async Task CloseDrawerWithAmount(decimal finalAmount)
        {
            FinalCashAmount = finalAmount;
            await CloseDrawerAsync();
        }

        public async Task PrintReportWithOptions(bool includeTransactions, bool includeFinancialSummary, bool printCashierCopy)
        {
            // Store the options
            IncludeTransactionDetails = includeTransactions;
            IncludeFinancialSummary = includeFinancialSummary;
            PrintCashierCopy = printCashierCopy;

            // Call the print method
            await PrintReportAsync();
        }

        public async Task RefreshDrawerDataAsync()
        {
            try
            {
                IsProcessing = true;
                Debug.WriteLine("Refreshing drawer data");

                CurrentDrawer = await _drawerService.GetCurrentDrawerAsync();

                if (CurrentDrawer == null)
                {
                    DrawerHistory.Clear();
                    ResetFinancialTotals();
                    UpdateStatus();
                    return;
                }

                await LoadDrawerHistoryAsync();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing drawer data: {ex.Message}");
                await ShowErrorMessageAsync("Error refreshing drawer data");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        #endregion

        #region Event Handling

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();

            // Single, consolidated event handler for all drawer-related events
            _eventAggregator.Subscribe<DrawerUpdateEvent>(HandleDrawerUpdate);
            _eventAggregator.Subscribe<EntityChangedEvent<DrawerDTO>>(HandleDrawerChanged);
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(HandleTransactionChanged);
            _eventAggregator.Subscribe<SupplierPaymentEvent>(HandleSupplierPayment);
        }

        protected override void UnsubscribeFromEvents()
        {
            base.UnsubscribeFromEvents();
            _eventAggregator.Unsubscribe<DrawerUpdateEvent>(HandleDrawerUpdate);
            _eventAggregator.Unsubscribe<EntityChangedEvent<DrawerDTO>>(HandleDrawerChanged);
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(HandleTransactionChanged);
            _eventAggregator.Unsubscribe<SupplierPaymentEvent>(HandleSupplierPayment);
        }

        private async void HandleDrawerUpdate(DrawerUpdateEvent evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    Debug.WriteLine($"Handling drawer update event: {evt.Type}");

                    // For transaction modifications, add a small delay
                    if (evt.Type == TransactionCalculator.ActionTypes.Modification)
                    {
                        await Task.Delay(200);
                    }

                    await RefreshDrawerDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling drawer update event: {ex.Message}");
                    await ShowErrorMessageAsync("Error updating drawer display");
                }
            });
        }

        private async void HandleDrawerChanged(EntityChangedEvent<DrawerDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshDrawerDataAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling drawer changed event: {ex.Message}");
                }
            });
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            if (evt.Entity != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await RefreshDrawerDataAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error handling transaction changed event: {ex.Message}");
                    }
                });
            }
        }

        private async void HandleSupplierPayment(SupplierPaymentEvent evt)
        {
            try
            {
                await RefreshDrawerDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling supplier payment event: {ex.Message}");
                await ShowErrorMessageAsync("Error updating drawer after supplier payment");
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateStatus()
        {
            if (CurrentDrawer == null)
            {
                StatusMessage = "No drawer is currently open";
                return;
            }

            var timeInfo = CurrentDrawer.Status == "Open"
                ? $"opened at {CurrentDrawer.OpenedAt:t}"
                : $"closed at {CurrentDrawer.ClosedAt:t}";

            StatusMessage = $"Drawer is {CurrentDrawer.Status.ToLower()} - {CurrentDrawer.CashierName} - {timeInfo}";
            OnPropertyChanged(nameof(StatusMessage));
        }

        protected override async Task LoadDataAsync()
        {
            await LoadInitialDataAsync();
        }

        public override void Dispose()
        {
            Debug.WriteLine("Disposing DrawerViewModel");
            base.Dispose();
        }

        #endregion

        #region Helper Classes

        public class DrawerSessionItem
        {
            public int DrawerId { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public string CashierName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;

            public string DisplayText =>
                $"Drawer {DrawerId} - {OpenedAt:MM/dd/yyyy} - {CashierName} ({Status})";
        }

        #endregion
    }
}