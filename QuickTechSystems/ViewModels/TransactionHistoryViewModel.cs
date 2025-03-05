using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Infrastructure.Data;
using QuickTechSystems.WPF.Commands;
using System.Linq;
using System.Data.Common;

namespace QuickTechSystems.WPF.ViewModels
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly ICategoryService _categoryService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private ObservableCollection<TransactionDTO> _transactions;
        private decimal _totalSales;
        private decimal _totalProfit;
        private string _searchText = string.Empty;
        private string _selectedDateRange;
        private bool _isRefreshing;
        private bool _isBusy;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private bool _isCustomDateRange;
        private ObservableCollection<CategoryDTO> _categories;
        private CategoryDTO? _selectedCategory;
        private Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;
        private DateTime _lastSearchTime = DateTime.MinValue;
        private const int DebounceDelayMs = 300;

        // Semaphore to prevent concurrent DbContext operations
        private readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(1, 1);

        public ObservableCollection<TransactionDTO> Transactions
        {
            get => _transactions;
            set => SetProperty(ref _transactions, value);
        }

        public decimal TotalSales
        {
            get => _totalSales;
            set => SetProperty(ref _totalSales, value);
        }

        public decimal TotalProfit
        {
            get => _totalProfit;
            set => SetProperty(ref _totalProfit, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value); // No direct LoadTransactions call here
        }

        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value); // No direct LoadTransactions call here
        }

        public bool IsCustomDateRange
        {
            get => _isCustomDateRange;
            set => SetProperty(ref _isCustomDateRange, value); // Managed via dialog
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public CategoryDTO? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _ = LoadTransactionsForRange();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    DebounceFilterTransactions();
                }
            }
        }

        public string SelectedDateRange
        {
            get => _selectedDateRange;
            set
            {
                if (SetProperty(ref _selectedDateRange, value))
                {
                    if (value == "Custom")
                    {
                        _ = ShowCustomDateRangeDialogAsync();
                    }
                    else
                    {
                        IsCustomDateRange = false;
                        _ = LoadTransactionsForRange();
                    }
                }
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ObservableCollection<string> DateRanges { get; } = new()
        {
            "Today",
            "Yesterday",
            "Last 7 Days",
            "Last 30 Days",
            "This Month",
            "Last Month",
            "This Year",
            "All Time",
            "Custom"
        };

        public ICommand ExportCommand { get; }
        public ICommand PrintReportCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand ClearFiltersCommand { get; }

        public TransactionHistoryViewModel(
            ITransactionService transactionService,
            ICategoryService categoryService,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _transactions = new ObservableCollection<TransactionDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _transactionChangedHandler = HandleTransactionChanged;

            ExportCommand = new AsyncRelayCommand(async _ => await ExportTransactionsAsync(), _ => !IsBusy);
            PrintReportCommand = new AsyncRelayCommand(async _ => await PrintTransactionReportAsync(), _ => !IsBusy);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync(), _ => !IsBusy);
            ViewTransactionDetailsCommand = new RelayCommand(ShowTransactionDetails, _ => !IsBusy);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters(), _ => !IsBusy);

            SelectedDateRange = "Today";

            InitializeDataAsync();
        }

        private async void InitializeDataAsync()
        {
            try
            {
                IsBusy = true;
                await LoadCategoriesAsync();
                await LoadTransactionsForRange();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Failed to initialize data: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowTransactionDetails(object parameter)
        {
            if (parameter is not TransactionDTO transaction)
            {
                MessageBox.Show("Please select a transaction to view details.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var detailWindow = new TransactionDetailWindow(transaction);
                detailWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowErrorMessageAsync($"Error showing transaction details: {ex.Message}");
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                await _dbSemaphore.WaitAsync();
                Debug.WriteLine("Starting to load categories");

                List<CategoryDTO> categories;
                using (var context = await _dbContextFactory.CreateDbContextAsync())
                {
                    categories = await context.Set<Category>()
                        .Where(c => c.IsActive)
                        .Select(c => new CategoryDTO
                        {
                            CategoryId = c.CategoryId,
                            Name = c.Name,
                            Description = c.Description,
                            IsActive = c.IsActive
                        })
                        .ToListAsync();
                }

                Debug.WriteLine($"Retrieved {categories.Count} categories from database");

                var allCategories = new List<CategoryDTO> { new CategoryDTO { CategoryId = 0, Name = "All Categories" } };
                allCategories.AddRange(categories ?? new List<CategoryDTO>());

                Categories = new ObservableCollection<CategoryDTO>(allCategories);
                SelectedCategory = Categories.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading categories: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading categories: {ex.Message}. Using default category.");
                Categories = new ObservableCollection<CategoryDTO> { new CategoryDTO { CategoryId = 0, Name = "All Categories" } };
                SelectedCategory = Categories.FirstOrDefault();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            try
            {
                await LoadTransactionsForRange();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling transaction change: {ex.Message}");
                await ShowErrorMessageAsync($"Error updating transactions: {ex.Message}");
            }
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                IsRefreshing = true;
                await LoadTransactionsForRange();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error refreshing data: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async Task LoadTransactionsForRange()
        {
            DateTime startDate, endDate;

            try
            {
                await _dbSemaphore.WaitAsync();
                IsBusy = true;

                if (IsCustomDateRange)
                {
                    startDate = StartDate;
                    endDate = EndDate.AddDays(1).AddSeconds(-1);
                }
                else
                {
                    switch (SelectedDateRange)
                    {
                        case "Today":
                            startDate = DateTime.Today;
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        case "Yesterday":
                            startDate = DateTime.Today.AddDays(-1);
                            endDate = DateTime.Today.AddSeconds(-1);
                            break;
                        case "Last 7 Days":
                            startDate = DateTime.Today.AddDays(-7);
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        case "Last 30 Days":
                            startDate = DateTime.Today.AddDays(-30);
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        case "This Month":
                            startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        case "Last Month":
                            startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
                            endDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddSeconds(-1);
                            break;
                        case "This Year":
                            startDate = new DateTime(DateTime.Today.Year, 1, 1);
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        case "All Time":
                            startDate = DateTime.MinValue;
                            endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                            break;
                        default:
                            Debug.WriteLine($"Invalid date range selected: {SelectedDateRange}");
                            return;
                    }
                }

                var transactions = await _transactionService.GetByDateRangeAsync(startDate, endDate) ?? new List<TransactionDTO>();
                var filteredTransactions = transactions;

                if (SelectedCategory != null && SelectedCategory.CategoryId != 0)
                {
                    filteredTransactions = transactions.Where(t =>
                        t.Details?.Any(d => d.CategoryId == SelectedCategory.CategoryId) ?? false).ToList();
                }

                Transactions = new ObservableCollection<TransactionDTO>(filteredTransactions.OrderByDescending(t => t.TransactionDate));
                CalculateTotals();
            }
            catch (DbException ex)
            {
                Debug.WriteLine($"Database error loading transactions: {ex.Message}");
                await ShowErrorMessageAsync($"Database error: {ex.Message}. Please check your connection and try again.");
                Transactions = new ObservableCollection<TransactionDTO>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading transactions: {ex.Message}");
                await ShowErrorMessageAsync($"Error loading transactions: {ex.Message}");
                Transactions = new ObservableCollection<TransactionDTO>();
            }
            finally
            {
                _dbSemaphore.Release();
                IsBusy = false;
            }
        }

        private async void DebounceFilterTransactions()
        {
            var now = DateTime.Now;
            if ((now - _lastSearchTime).TotalMilliseconds < DebounceDelayMs)
            {
                await Task.Delay(DebounceDelayMs - (int)(now - _lastSearchTime).TotalMilliseconds);
            }

            _lastSearchTime = DateTime.Now;
            FilterTransactions();
        }

        private void FilterTransactions()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SearchText) && (SelectedCategory == null || SelectedCategory.CategoryId == 0))
                {
                    LoadTransactionsForRange();
                    return;
                }

                var filtered = Transactions.Where(t =>
                    (string.IsNullOrWhiteSpace(SearchText) ||
                     (t.CustomerName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                     t.TransactionId.ToString().Contains(SearchText) ||
                     (t.Details?.Any(d => d.ProductName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ?? false)) &&
                    (SelectedCategory == null ||
                     SelectedCategory.CategoryId == 0 ||
                     (t.Details?.Any(d => d.CategoryId == SelectedCategory.CategoryId) ?? false))
                ).ToList();

                Transactions = new ObservableCollection<TransactionDTO>(filtered);
                CalculateTotals();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filtering transactions: {ex.Message}");
                ShowErrorMessageAsync($"Error filtering transactions: {ex.Message}");
            }
        }

        private void CalculateTotals()
        {
            try
            {
                TotalSales = Transactions?.Sum(t => t.TotalAmount) ?? 0;
                TotalProfit = Transactions?.Sum(t => t.Details?.Sum(d => (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0) ?? 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating totals: {ex.Message}");
                TotalSales = 0;
                TotalProfit = 0;
            }
        }

        private void ClearFilters()
        {
            try
            {
                SearchText = string.Empty;
                SelectedCategory = Categories?.FirstOrDefault(c => c.CategoryId == 0);
                SelectedDateRange = "Today";
                IsCustomDateRange = false;
                StartDate = DateTime.Today;
                EndDate = DateTime.Today;
                _ = LoadTransactionsForRange();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing filters: {ex.Message}");
                ShowErrorMessageAsync($"Error resetting filters: {ex.Message}");
            }
        }

        private async Task ExportTransactionsAsync()
        {
            try
            {
                if (Transactions == null || !Transactions.Any())
                {
                    MessageBox.Show("No transactions available to export.", "Export Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"Transaction_History_{DateTime.Now:yyyyMMdd}"
                };

                if (saveFileDialog.ShowDialog() != true) return;

                IsBusy = true;
                var csv = new StringBuilder();
                csv.AppendLine("Transaction ID,Date,Customer,Type,Items,Total Amount,Profit,Status");

                foreach (var transaction in Transactions)
                {
                    var profit = transaction.Details?.Sum(d => (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0;
                    var itemCount = transaction.Details?.Count ?? 0;

                    csv.AppendLine($"{transaction.TransactionId}," +
                        $"\"{transaction.TransactionDate:g}\"," +
                        $"\"{transaction.CustomerName?.Replace("\"", "\"\"") ?? "N/A"}\"," +
                        $"{transaction.TransactionType}," +
                        $"{itemCount}," +
                        $"{transaction.TotalAmount:F2}," +
                        $"{profit:F2}," +
                        $"{transaction.Status}");
                }

                await File.WriteAllTextAsync(saveFileDialog.FileName, csv.ToString());
                MessageBox.Show($"Transactions exported successfully to {saveFileDialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                await ShowErrorMessageAsync($"Error saving file: {ex.Message}. Ensure the file is not in use.");
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error exporting transactions: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PrintTransactionReportAsync()
        {
            try
            {
                if (Transactions == null || !Transactions.Any())
                {
                    MessageBox.Show("No transactions available to print.", "Print Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                IsBusy = true;
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true) return;

                var document = new FlowDocument
                {
                    PageWidth = printDialog.PrintableAreaWidth,
                    PageHeight = printDialog.PrintableAreaHeight,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = 12
                };

                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Bold(new Run("Transaction History Report\n")) { FontSize = 18 });
                paragraph.Inlines.Add(new Run($"Period: {SelectedDateRange}\n\n"));

                paragraph.Inlines.Add(new Bold(new Run("Summary:\n")));
                paragraph.Inlines.Add(new Run($"Total Transactions: {Transactions.Count}\n"));
                paragraph.Inlines.Add(new Run($"Total Sales: {TotalSales:C}\n"));
                paragraph.Inlines.Add(new Run($"Total Profit: {TotalProfit:C}\n\n"));

                var table = new Table();
                var columns = new[] { 80.0, 120.0, 150.0, 100.0, 100.0 }
                    .Select(width => new TableColumn { Width = new GridLength(width) })
                    .ToList();
                columns.ForEach(col => table.Columns.Add(col));

                var headerRow = new TableRow();
                foreach (var header in new[] { "ID", "Date", "Customer", "Total", "Status" })
                {
                    headerRow.Cells.Add(new TableCell(new Paragraph(new Run(header)) { FontWeight = FontWeights.Bold }));
                }

                table.RowGroups.Add(new TableRowGroup());
                table.RowGroups[0].Rows.Add(headerRow);

                foreach (var transaction in Transactions)
                {
                    var row = new TableRow();
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TransactionId.ToString()))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TransactionDate.ToString("g")))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.CustomerName ?? "N/A"))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TotalAmount.ToString("C")))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Status.ToString()))));
                    table.RowGroups[0].Rows.Add(row);

                    if (transaction.Details?.Any() == true)
                    {
                        var detailsRow = new TableRow();
                        var detailsCell = new TableCell { ColumnSpan = 5 };
                        var detailsParagraph = new Paragraph { TextIndent = 20 };

                        foreach (var detail in transaction.Details)
                        {
                            detailsParagraph.Inlines.Add(new Run(
                                $"• {detail.ProductName} - Qty: {detail.Quantity} @ {detail.UnitPrice:C} = {detail.Total:C}\n"));
                        }

                        detailsCell.Blocks.Add(detailsParagraph);
                        detailsRow.Cells.Add(detailsCell);
                        table.RowGroups[0].Rows.Add(detailsRow);
                    }
                }

                document.Blocks.Add(paragraph);
                document.Blocks.Add(table);

                printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,
                    "Transaction History Report");
                MessageBox.Show("Report printed successfully.", "Print Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing report: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ShowCustomDateRangeDialogAsync()
        {
            try
            {
                IsBusy = true;
                var window = new Window
                {
                    Title = "Select Custom Date Range",
                    Width = 300,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize
                };

                var stackPanel = new StackPanel { Margin = new Thickness(10) };

                // Start Date
                stackPanel.Children.Add(new TextBlock { Text = "Start Date:", Margin = new Thickness(0, 0, 0, 5) });
                var startDatePicker = new DatePicker
                {
                    SelectedDate = StartDate,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(startDatePicker);

                // End Date
                stackPanel.Children.Add(new TextBlock { Text = "End Date:", Margin = new Thickness(0, 0, 0, 5) });
                var endDatePicker = new DatePicker
                {
                    SelectedDate = EndDate,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(endDatePicker);

                // OK Button
                var okButton = new Button
                {
                    Content = "OK",
                    Width = 100,
                    Margin = new Thickness(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                bool? dialogResult = null;
                okButton.Click += (s, e) =>
                {
                    if (startDatePicker.SelectedDate.HasValue && endDatePicker.SelectedDate.HasValue)
                    {
                        StartDate = startDatePicker.SelectedDate.Value;
                        EndDate = endDatePicker.SelectedDate.Value;
                        if (StartDate <= EndDate)
                        {
                            IsCustomDateRange = true;
                            dialogResult = true;
                            window.Close();
                        }
                        else
                        {
                            ShowErrorMessageAsync("Start date cannot be after end date.");
                        }
                    }
                    else
                    {
                        ShowErrorMessageAsync("Please select both start and end dates.");
                    }
                };

                stackPanel.Children.Add(okButton);
                window.Content = stackPanel;

                // Show the dialog modally
                window.ShowDialog();

                if (dialogResult == true)
                {
                    await LoadTransactionsForRange();
                }
                else
                {
                    // Reset to default if canceled
                    SelectedDateRange = "Today";
                    IsCustomDateRange = false;
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error showing custom date range dialog: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}