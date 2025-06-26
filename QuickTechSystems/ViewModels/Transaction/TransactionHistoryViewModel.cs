using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace QuickTechSystems.ViewModels.Transaction
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IEmployeeService _employeeService;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _dataLock;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isDisposed = false;

        private ObservableCollection<ExtendedTransactionDTO> _allTransactions;
        private ObservableCollection<ExtendedTransactionDTO> _filteredTransactions;
        private ObservableCollection<ExtendedTransactionDTO> _pagedTransactions;
        private ObservableCollection<EmployeeDTO> _employees;
        private ExtendedTransactionDTO? _selectedTransaction;
        private string? _selectedEmployeeId;
        private string? _selectedTransactionType;
        private decimal _totalSalesAmount;
        private bool _isLoading;
        private TransactionDetailsPopupViewModel? _currentPopupViewModel;

        // Date range filter properties
        private DateTime? _startDate;
        private DateTime? _endDate;

        // Pagination properties
        private int _currentPage = 1;
        private int _itemsPerPage = 50;
        private int _totalPages = 1;
        private int _totalItems = 0;
        private List<int> _pageSizes = new() { 50, 200, 1000 };

        private static readonly Dictionary<string, Func<ExtendedTransactionDTO, bool>> TransactionTypeFilters = new()
        {
            { "All Types", _ => true },
            { "Sale", t => t.TransactionType == TransactionType.Sale && !IsDebtTransaction(t) },
            { "By Dept", t => t.TransactionType == TransactionType.Sale && IsDebtTransaction(t) }
        };

        public ObservableCollection<ExtendedTransactionDTO> PagedTransactions
        {
            get => _pagedTransactions;
            set => SetProperty(ref _pagedTransactions, value);
        }

        public ObservableCollection<EmployeeDTO> Employees
        {
            get => _employees;
            set => SetProperty(ref _employees, value);
        }

        public ExtendedTransactionDTO? SelectedTransaction
        {
            get => _selectedTransaction;
            set => SetProperty(ref _selectedTransaction, value);
        }

        public string? SelectedEmployeeId
        {
            get => _selectedEmployeeId;
            set
            {
                if (SetProperty(ref _selectedEmployeeId, value))
                {
                    _ = ApplyFiltersAndPaginationAsync();
                }
            }
        }

        public string? SelectedTransactionType
        {
            get => _selectedTransactionType;
            set
            {
                if (SetProperty(ref _selectedTransactionType, value))
                {
                    _ = ApplyFiltersAndPaginationAsync();
                }
            }
        }

        public decimal TotalSalesAmount
        {
            get => _totalSalesAmount;
            set => SetProperty(ref _totalSalesAmount, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // Date Range Properties
        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    CurrentPage = 1;
                    _ = ApplyFiltersAndPaginationAsync();
                }
            }
        }

        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    CurrentPage = 1;
                    _ = ApplyFiltersAndPaginationAsync();
                }
            }
        }

        // Pagination Properties
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    _ = UpdatePagedDataAsync();
                    OnPropertyChanged(nameof(CanGoToPreviousPage));
                    OnPropertyChanged(nameof(CanGoToNextPage));
                    OnPropertyChanged(nameof(PageInfo));
                }
            }
        }

        public int ItemsPerPage
        {
            get => _itemsPerPage;
            set
            {
                if (SetProperty(ref _itemsPerPage, value))
                {
                    CurrentPage = 1;
                    _ = ApplyFiltersAndPaginationAsync();
                }
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set => SetProperty(ref _totalPages, value);
        }

        public int TotalItems
        {
            get => _totalItems;
            set => SetProperty(ref _totalItems, value);
        }

        public List<int> PageSizes
        {
            get => _pageSizes;
            set => SetProperty(ref _pageSizes, value);
        }

        public bool CanGoToPreviousPage => CurrentPage > 1;
        public bool CanGoToNextPage => CurrentPage < TotalPages;

        public string PageInfo => $"Page {CurrentPage} of {TotalPages} ({TotalItems} total items)";

        public bool CanExecuteCommands => !IsLoading && !_isDisposed;

        public List<string> TransactionTypes => new() { "All Types", "Sale", "By Dept" };

        public ICommand LoadDataCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand DeleteTransactionCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ICommand PrintReportCommand { get; }

        public TransactionHistoryViewModel(
            ITransactionService transactionService,
            IEmployeeService employeeService,
            IServiceProvider serviceProvider,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
            : base(eventAggregator, dbContextScopeService)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _allTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _filteredTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _pagedTransactions = new ObservableCollection<ExtendedTransactionDTO>();
            _employees = new ObservableCollection<EmployeeDTO>();
            _dataLock = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();

            LoadDataCommand = new RelayCommand(async _ => await LoadDataAsync(), _ => CanExecuteCommands);
            ClearFiltersCommand = new RelayCommand(ClearFilters, _ => CanExecuteCommands);
            ViewTransactionDetailsCommand = new RelayCommand(async parameter => await OpenTransactionDetailsAsync(parameter), CanExecuteTransactionCommand);
            DeleteTransactionCommand = new RelayCommand(async parameter => await DeleteTransactionAsync(parameter), CanExecuteTransactionCommand);

            // Pagination Commands
            NextPageCommand = new RelayCommand(_ => { if (CanGoToNextPage) CurrentPage++; }, _ => CanGoToNextPage && CanExecuteCommands);
            PreviousPageCommand = new RelayCommand(_ => { if (CanGoToPreviousPage) CurrentPage--; }, _ => CanGoToPreviousPage && CanExecuteCommands);
            FirstPageCommand = new RelayCommand(_ => CurrentPage = 1, _ => CanGoToPreviousPage && CanExecuteCommands);
            LastPageCommand = new RelayCommand(_ => CurrentPage = TotalPages, _ => CanGoToNextPage && CanExecuteCommands);
            GoToPageCommand = new RelayCommand(async parameter => await GoToPageAsync(parameter), _ => CanExecuteCommands);
            PrintReportCommand = new RelayCommand(async _ => await PrintSalesReportAsync(), _ => CanExecuteCommands && _filteredTransactions?.Any() == true);

            SelectedTransactionType = "All Types";
        }

        protected override async Task LoadDataImplementationAsync()
        {
            if (_isDisposed) return;

            await _dataLock.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                IsLoading = true;

                // Load data concurrently but handle results separately
                var transactionsTask = LoadTransactionsAsync();
                var employeesTask = LoadEmployeesAsync();

                await Task.WhenAll(transactionsTask, employeesTask);

                var transactions = await transactionsTask;
                var employees = await employeesTask;

                await UpdateUIAsync(() =>
                {
                    _allTransactions.Clear();
                    foreach (var transaction in transactions ?? Enumerable.Empty<ExtendedTransactionDTO>())
                    {
                        _allTransactions.Add(transaction);
                    }

                    Employees.Clear();
                    Employees.Add(new EmployeeDTO { EmployeeId = 0, FirstName = "All", LastName = "Employees" });
                    foreach (var employee in employees?.Where(e => e.IsActive) ?? Enumerable.Empty<EmployeeDTO>())
                    {
                        Employees.Add(employee);
                    }
                });

                await ApplyFiltersAndPaginationAsync();
                await CalculateTotalSalesAsync();
            }
            finally
            {
                IsLoading = false;
                _dataLock.Release();
            }
        }

        private async Task<IEnumerable<ExtendedTransactionDTO>?> LoadTransactionsAsync()
        {
            try
            {
                var transactions = await ExecuteDbOperationAsync(() => _transactionService.GetAllAsync(), "Loading transactions");
                return transactions?.Select(t => (ExtendedTransactionDTO)t);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading transactions: {ex}");
                return null;
            }
        }

        private async Task<IEnumerable<EmployeeDTO>?> LoadEmployeesAsync()
        {
            try
            {
                return await ExecuteDbOperationAsync(() => _employeeService.GetAllAsync(), "Loading employees");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading employees: {ex}");
                return null;
            }
        }

        private async Task ApplyFiltersAndPaginationAsync()
        {
            if (_isDisposed || _allTransactions == null) return;

            try
            {
                var filtered = _allTransactions.AsEnumerable();

                // Date range filter
                if (StartDate.HasValue)
                {
                    filtered = filtered.Where(t => t.TransactionDate.Date >= StartDate.Value.Date);
                }

                if (EndDate.HasValue)
                {
                    filtered = filtered.Where(t => t.TransactionDate.Date <= EndDate.Value.Date);
                }

                // Employee filter
                if (!string.IsNullOrEmpty(SelectedEmployeeId) && SelectedEmployeeId != "0")
                {
                    filtered = filtered.Where(t => t.CashierId == SelectedEmployeeId);
                }

                // Transaction type filter
                if (!string.IsNullOrEmpty(SelectedTransactionType) && TransactionTypeFilters.ContainsKey(SelectedTransactionType))
                {
                    filtered = filtered.Where(TransactionTypeFilters[SelectedTransactionType]);
                }

                var filteredList = filtered.OrderByDescending(t => t.TransactionDate).ToList();

                await UpdateUIAsync(() =>
                {
                    _filteredTransactions.Clear();
                    foreach (var transaction in filteredList)
                    {
                        _filteredTransactions.Add(transaction);
                    }

                    TotalItems = filteredList.Count;
                    TotalPages = Math.Max(1, (int)Math.Ceiling((double)TotalItems / ItemsPerPage));

                    // Ensure current page is valid
                    if (CurrentPage > TotalPages)
                    {
                        CurrentPage = TotalPages;
                    }

                    OnPropertyChanged(nameof(CanGoToPreviousPage));
                    OnPropertyChanged(nameof(CanGoToNextPage));
                    OnPropertyChanged(nameof(PageInfo));
                });

                await UpdatePagedDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying filters and pagination: {ex}");
            }
        }

        private async Task UpdatePagedDataAsync()
        {
            if (_isDisposed || _filteredTransactions == null) return;

            try
            {
                var skip = (CurrentPage - 1) * ItemsPerPage;
                var pagedData = _filteredTransactions.Skip(skip).Take(ItemsPerPage).ToList();

                await UpdateUIAsync(() =>
                {
                    PagedTransactions.Clear();
                    foreach (var transaction in pagedData)
                    {
                        PagedTransactions.Add(transaction);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating paged data: {ex}");
            }
        }

        private async Task CalculateTotalSalesAsync()
        {
            try
            {
                await UpdateUIAsync(() =>
                {
                    // Calculate total from filtered transactions (all pages)
                    TotalSalesAmount = _filteredTransactions?
                        .Where(t => t.TransactionType == TransactionType.Sale && t.Status == TransactionStatus.Completed)
                        .Sum(t => t.TotalAmount) ?? 0;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating total sales: {ex}");
            }
        }

        private async Task GoToPageAsync(object? parameter)
        {
            if (parameter is string pageStr && int.TryParse(pageStr, out int page))
            {
                if (page >= 1 && page <= TotalPages)
                {
                    CurrentPage = page;
                }
            }
        }

        private void ClearFilters(object? parameter)
        {
            StartDate = null;
            EndDate = null;
            SelectedEmployeeId = null;
            SelectedTransactionType = "All Types";
            CurrentPage = 1;
        }

        private async Task OpenTransactionDetailsAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction || _isDisposed) return;

            try
            {
                var popupViewModel = ActivatorUtilities.CreateInstance<TransactionDetailsPopupViewModel>(_serviceProvider);
                var popupWindow = new TransactionDetailsPopup
                {
                    DataContext = popupViewModel,
                    Owner = System.Windows.Application.Current.MainWindow
                };

                popupViewModel.SetView(popupWindow);
                popupViewModel.RequestClose += (_, _) => SafeCloseWindow(popupWindow);
                popupViewModel.TransactionChanged += async (_, args) => await RefreshDataAfterChange(args.TransactionId);

                await popupViewModel.InitializeAsync(transaction);
                _currentPopupViewModel = popupViewModel;

                popupWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error opening transaction details", ex);
            }
            finally
            {
                _currentPopupViewModel?.Dispose();
                _currentPopupViewModel = null;
            }
        }

        private async Task DeleteTransactionAsync(object? parameter)
        {
            if (parameter is not ExtendedTransactionDTO transaction || _isDisposed) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete Transaction #{transaction.TransactionId}?\n\nThis will permanently remove the transaction and restock all sold items.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsLoading = true;
                var success = await ExecuteDbOperationAsync(() => _transactionService.DeleteTransactionWithRestockAsync(transaction.TransactionId), "Deleting transaction");

                if (success)
                {
                    await ShowSuccessMessage("Transaction deleted successfully and items restocked!");
                    SelectedTransaction = null;
                    await LoadDataAsync();
                }
                else
                {
                    ShowTemporaryErrorMessage("Failed to delete transaction.");
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync("Error deleting transaction", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshDataAfterChange(int transactionId)
        {
            try
            {
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing data: {ex}");
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator?.Subscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator?.Unsubscribe<EntityChangedEvent<TransactionDTO>>(OnTransactionChanged);
        }

        private async void OnTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            if (_isDisposed || evt?.Entity == null) return;

            try
            {
                await UpdateUIAsync(async () =>
                {
                    var extendedTransaction = (ExtendedTransactionDTO)evt.Entity;

                    switch (evt.Action)
                    {
                        case "Create":
                            if (!_allTransactions.Any(t => t.TransactionId == evt.Entity.TransactionId))
                            {
                                _allTransactions.Insert(0, extendedTransaction);
                            }
                            break;

                        case "Update":
                            var existingIndex = _allTransactions.ToList().FindIndex(t => t.TransactionId == evt.Entity.TransactionId);
                            if (existingIndex >= 0)
                            {
                                _allTransactions[existingIndex] = extendedTransaction;
                            }
                            break;

                        case "Delete":
                            var toRemove = _allTransactions.FirstOrDefault(t => t.TransactionId == evt.Entity.TransactionId);
                            if (toRemove != null)
                            {
                                _allTransactions.Remove(toRemove);
                            }
                            break;
                    }

                    await ApplyFiltersAndPaginationAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling transaction changed event: {ex}");
            }
        }

        private async Task UpdateUIAsync(Action action)
        {
            if (_isDisposed) return;

            try
            {
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating UI: {ex}");
            }
        }

        private static void SafeCloseWindow(Window? window)
        {
            try
            {
                window?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing window: {ex}");
            }
        }

        private bool CanExecuteTransactionCommand(object? parameter)
        {
            return parameter is ExtendedTransactionDTO && CanExecuteCommands;
        }

        private static bool IsDebtTransaction(ExtendedTransactionDTO transaction)
        {
            return string.Equals(transaction.PaymentMethod, "debt", StringComparison.OrdinalIgnoreCase);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _isDisposed = true;

                try
                {
                    _cancellationTokenSource?.Cancel();
                    _currentPopupViewModel?.Dispose();
                    _dataLock?.Dispose();
                    _cancellationTokenSource?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during disposal: {ex}");
                }
            }

            base.Dispose(disposing);
        }

        #region Print Sales Report

        private async Task PrintSalesReportAsync()
        {
            if (_filteredTransactions == null || !_filteredTransactions.Any())
            {
                MessageBox.Show("No transactions to print. Please adjust your filters.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsLoading = true;

                await Task.Run(async () =>
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            // Check for printer availability
                            bool printerAvailable = false;
                            try
                            {
                                System.Printing.PrintServer printServer = new System.Printing.PrintServer();
                                var printQueues = printServer.GetPrintQueues();
                                printerAvailable = printQueues.Any();
                            }
                            catch
                            {
                                printerAvailable = false;
                            }

                            if (!printerAvailable)
                            {
                                MessageBox.Show("No printer available. Please connect a printer and try again.", "Printer Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            // Show print dialog
                            var printDialog = new System.Windows.Controls.PrintDialog();
                            if (printDialog.ShowDialog() != true)
                                return;

                            // Generate report data
                            var reportData = await GenerateReportDataAsync();

                            // Create and print the report document
                            var reportDocument = CreateSalesReportDocument(printDialog, reportData);
                            printDialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)reportDocument).DocumentPaginator, $"Sales Report - {DateTime.Now:yyyy-MM-dd}");

                            MessageBox.Show("Sales report printed successfully!", "Print Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error printing sales report: {ex}");
                            MessageBox.Show($"Error printing report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<SalesReportData> GenerateReportDataAsync()
        {
            var reportData = new SalesReportData
            {
                StartDate = StartDate,
                EndDate = EndDate,
                GeneratedDate = DateTime.Now,
                TotalTransactions = _filteredTransactions.Count
            };

            // Calculate totals by payment method
            var completedTransactions = _filteredTransactions.Where(t => t.Status == TransactionStatus.Completed).ToList();

            reportData.TotalSales = completedTransactions.Sum(t => t.TotalAmount);
            reportData.CashSales = completedTransactions.Where(t => !IsDebtTransaction(t)).Sum(t => t.TotalAmount);
            reportData.DebtSales = completedTransactions.Where(t => IsDebtTransaction(t)).Sum(t => t.TotalAmount);

            // Group products and calculate quantities
            var productSummary = new Dictionary<string, ProductSalesInfo>();

            foreach (var transaction in completedTransactions)
            {
                if (transaction.Details != null)
                {
                    foreach (var detail in transaction.Details)
                    {
                        var productName = detail.ProductName ?? $"Product {detail.ProductId}";

                        if (!productSummary.ContainsKey(productName))
                        {
                            productSummary[productName] = new ProductSalesInfo
                            {
                                ProductName = productName,
                                TotalQuantity = 0,
                                TotalAmount = 0
                            };
                        }

                        productSummary[productName].TotalQuantity += detail.Quantity;
                        productSummary[productName].TotalAmount += detail.Total;
                    }
                }
            }

            reportData.ProductSales = productSummary.Values.OrderByDescending(p => p.TotalAmount).ToList();

            // Get business settings for header
            try
            {
                var businessSettingsService = _serviceProvider.GetService<IBusinessSettingsService>();
                if (businessSettingsService != null)
                {
                    reportData.CompanyName = await businessSettingsService.GetSettingValueAsync("CompanyName", "Your Business Name");
                    reportData.Address = await businessSettingsService.GetSettingValueAsync("Address", "Your Business Address");
                    reportData.Phone = await businessSettingsService.GetSettingValueAsync("Phone", "Your Phone Number");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading business settings: {ex}");
                reportData.CompanyName = "Your Business Name";
                reportData.Address = "Your Business Address";
                reportData.Phone = "Your Phone Number";
            }

            return reportData;
        }

        private System.Windows.Documents.FlowDocument CreateSalesReportDocument(System.Windows.Controls.PrintDialog printDialog, SalesReportData reportData)
        {
            var flowDocument = new System.Windows.Documents.FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Arial"),
                FontWeight = System.Windows.FontWeights.Normal,
                PagePadding = new System.Windows.Thickness(20),
                TextAlignment = System.Windows.TextAlignment.Left
            };

            // Header
            var header = new System.Windows.Documents.Paragraph
            {
                TextAlignment = System.Windows.TextAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 0, 20)
            };

            header.Inlines.Add(new System.Windows.Documents.Run(reportData.CompanyName)
            {
                FontSize = 20,
                FontWeight = System.Windows.FontWeights.Bold
            });
            header.Inlines.Add(new System.Windows.Documents.LineBreak());
            header.Inlines.Add(new System.Windows.Documents.Run("Sales Report")
            {
                FontSize = 16,
                FontWeight = System.Windows.FontWeights.Bold
            });

            flowDocument.Blocks.Add(header);

            // Report info
            var infoTable = new System.Windows.Documents.Table { FontSize = 12, CellSpacing = 0 };
            infoTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(120) });
            infoTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = System.Windows.GridLength.Auto });
            infoTable.RowGroups.Add(new System.Windows.Documents.TableRowGroup());

            AddReportInfoRow(infoTable, "Generated:", reportData.GeneratedDate.ToString("MM/dd/yyyy hh:mm tt"));
            if (reportData.StartDate.HasValue || reportData.EndDate.HasValue)
            {
                string dateRange = "";
                if (reportData.StartDate.HasValue && reportData.EndDate.HasValue)
                {
                    dateRange = $"{reportData.StartDate.Value:MM/dd/yyyy} - {reportData.EndDate.Value:MM/dd/yyyy}";
                }
                else if (reportData.StartDate.HasValue)
                {
                    dateRange = $"From {reportData.StartDate.Value:MM/dd/yyyy}";
                }
                else if (reportData.EndDate.HasValue)
                {
                    dateRange = $"Until {reportData.EndDate.Value:MM/dd/yyyy}";
                }

                if (!string.IsNullOrEmpty(dateRange))
                {
                    AddReportInfoRow(infoTable, "Date Range:", dateRange);
                }
            }

            flowDocument.Blocks.Add(infoTable);
            flowDocument.Blocks.Add(CreateReportDivider());

            // Sales Summary
            var summaryTable = new System.Windows.Documents.Table { FontSize = 14, CellSpacing = 0, Margin = new System.Windows.Thickness(0, 10, 0, 10) };
            summaryTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(2, System.Windows.GridUnitType.Star) });
            summaryTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            summaryTable.RowGroups.Add(new System.Windows.Documents.TableRowGroup());

            // Header
            var summaryHeaderRow = new System.Windows.Documents.TableRow { Background = System.Windows.Media.Brushes.LightGray };
            var summaryHeaderCell = new System.Windows.Documents.TableCell(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("Sales Summary")))
            {
                TextAlignment = System.Windows.TextAlignment.Center,
                FontSize = 16
            });
            summaryHeaderCell.ColumnSpan = 2;
            summaryHeaderRow.Cells.Add(summaryHeaderCell);
            summaryTable.RowGroups[0].Rows.Add(summaryHeaderRow);

            AddReportTotalRow(summaryTable, "Total Transactions:", reportData.TotalTransactions.ToString());
            AddReportTotalRow(summaryTable, "Total Sales:", $"${reportData.TotalSales:N2}", true);
            AddReportTotalRow(summaryTable, "Cash Sales:", $"${reportData.CashSales:N2}");
            AddReportTotalRow(summaryTable, "Debt Sales:", $"${reportData.DebtSales:N2}");

            flowDocument.Blocks.Add(summaryTable);
            flowDocument.Blocks.Add(CreateReportDivider());

            // Product Sales Details
            if (reportData.ProductSales.Any())
            {
                var productHeader = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Product Sales Details")
                {
                    FontSize = 16,
                    FontWeight = System.Windows.FontWeights.Bold
                })
                {
                    Margin = new System.Windows.Thickness(0, 20, 0, 10)
                };
                flowDocument.Blocks.Add(productHeader);

                var productTable = new System.Windows.Documents.Table { FontSize = 11, CellSpacing = 0 };
                productTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(3, System.Windows.GridUnitType.Star) });
                productTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                productTable.Columns.Add(new System.Windows.Documents.TableColumn { Width = new System.Windows.GridLength(1.5, System.Windows.GridUnitType.Star) });
                productTable.RowGroups.Add(new System.Windows.Documents.TableRowGroup());

                // Product table header
                var productHeaderRow = new System.Windows.Documents.TableRow { Background = System.Windows.Media.Brushes.LightGray };
                productHeaderRow.Cells.Add(CreateReportCell("Product", System.Windows.FontWeights.Bold, System.Windows.TextAlignment.Left));
                productHeaderRow.Cells.Add(CreateReportCell("Quantity", System.Windows.FontWeights.Bold, System.Windows.TextAlignment.Center));
                productHeaderRow.Cells.Add(CreateReportCell("Total Sales", System.Windows.FontWeights.Bold, System.Windows.TextAlignment.Right));
                productTable.RowGroups[0].Rows.Add(productHeaderRow);

                // Add product rows
                foreach (var product in reportData.ProductSales)
                {
                    var row = new System.Windows.Documents.TableRow();
                    row.Cells.Add(CreateReportCell(product.ProductName, System.Windows.FontWeights.Normal, System.Windows.TextAlignment.Left));
                    row.Cells.Add(CreateReportCell(product.TotalQuantity.ToString("N0"), System.Windows.FontWeights.Normal, System.Windows.TextAlignment.Center));
                    row.Cells.Add(CreateReportCell($"${product.TotalAmount:N2}", System.Windows.FontWeights.Normal, System.Windows.TextAlignment.Right));
                    productTable.RowGroups[0].Rows.Add(row);
                }

                flowDocument.Blocks.Add(productTable);
            }

            return flowDocument;
        }

        private void AddReportInfoRow(System.Windows.Documents.Table table, string label, string value)
        {
            var row = new System.Windows.Documents.TableRow();
            row.Cells.Add(CreateReportCell(label, System.Windows.FontWeights.Bold));
            row.Cells.Add(CreateReportCell(value, System.Windows.FontWeights.Normal));
            table.RowGroups[0].Rows.Add(row);
        }

        private void AddReportTotalRow(System.Windows.Documents.Table table, string label, string value, bool isBold = false)
        {
            var row = new System.Windows.Documents.TableRow();
            var fontWeight = isBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            row.Cells.Add(CreateReportCell(label, fontWeight, System.Windows.TextAlignment.Left));
            row.Cells.Add(CreateReportCell(value, fontWeight, System.Windows.TextAlignment.Right));
            table.RowGroups[0].Rows.Add(row);
        }

        private System.Windows.Documents.TableCell CreateReportCell(string text, System.Windows.FontWeight fontWeight = default, System.Windows.TextAlignment alignment = System.Windows.TextAlignment.Left)
        {
            var paragraph = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(text ?? string.Empty))
            {
                FontWeight = fontWeight == default ? System.Windows.FontWeights.Normal : fontWeight,
                TextAlignment = alignment,
                Margin = new System.Windows.Thickness(5, 2, 5, 2)
            };
            return new System.Windows.Documents.TableCell(paragraph);
        }

        private System.Windows.Documents.BlockUIContainer CreateReportDivider()
        {
            return new System.Windows.Documents.BlockUIContainer(new System.Windows.Controls.Border
            {
                Height = 1,
                Background = System.Windows.Media.Brushes.Black,
                Margin = new System.Windows.Thickness(0, 10, 0, 10)
            });
        }

        #endregion

        #region Support Classes

        private class SalesReportData
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public DateTime GeneratedDate { get; set; }
            public string CompanyName { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public int TotalTransactions { get; set; }
            public decimal TotalSales { get; set; }
            public decimal CashSales { get; set; }
            public decimal DebtSales { get; set; }
            public List<ProductSalesInfo> ProductSales { get; set; } = new();
        }

        private class ProductSalesInfo
        {
            public string ProductName { get; set; } = string.Empty;
            public decimal TotalQuantity { get; set; }
            public decimal TotalAmount { get; set; }
        }

        #endregion
    }
}