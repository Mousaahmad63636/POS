using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.Collections.Generic;
using System.Diagnostics;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using QuickTechSystems.WPF.Helpers;

namespace QuickTechSystems.ViewModels.Expense
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private readonly ICategoryService _categoryService;
        private readonly IBusinessSettingsService _businessSettingsService;

        private ObservableCollection<ExpenseDTO> _expenses;
        private ObservableCollection<ExpenseDTO> _filteredExpenses;
        private ObservableCollection<string> _categories;
        private ExpenseDTO _selectedExpense;
        private ExpenseDTO _currentExpense;

        private string _searchText = string.Empty;
        private string _selectedCategory = "All";
        private DateTime _startDate = DateTime.Today.AddDays(-30);
        private DateTime _endDate = DateTime.Today;
        private bool _isLoading;
        private bool _isEditMode;

        // Analytics Properties
        private decimal _totalAmount;
        private int _totalCount;
        private decimal _averageAmount;
        private string _topCategory = "N/A";
        private double _monthlyGrowth;
        private bool _monthlyGrowthPositive;

        // Report Properties
        private bool _showReports;
        private DateTime _reportStartDate = DateTime.Today.AddDays(-30);
        private DateTime _reportEndDate = DateTime.Today;
        private string _loadingMessage = "Loading...";

        public ExpenseViewModel(
            IExpenseService expenseService,
            ICategoryService categoryService,
            IEventAggregator eventAggregator,
            IBusinessSettingsService businessSettingsService) : base(eventAggregator)
        {
            _expenseService = expenseService;
            _categoryService = categoryService;
            _businessSettingsService = businessSettingsService;

            _expenses = new ObservableCollection<ExpenseDTO>();
            _filteredExpenses = new ObservableCollection<ExpenseDTO>();
            _categories = new ObservableCollection<string>();
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };

            InitializeCommands();
            LoadDataAsync();
        }

        #region Properties

        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _filteredExpenses;
            set => SetProperty(ref _filteredExpenses, value);
        }

        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ExpenseDTO SelectedExpense
        {
            get => _selectedExpense;
            set => SetProperty(ref _selectedExpense, value);
        }

        public ExpenseDTO CurrentExpense
        {
            get => _currentExpense;
            set => SetProperty(ref _currentExpense, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilters();
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    ApplyFilters();
            }
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                    ApplyFilters();
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                    ApplyFilters();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set => SetProperty(ref _isEditMode, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            set => SetProperty(ref _totalAmount, value);
        }

        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public decimal AverageAmount
        {
            get => _averageAmount;
            set => SetProperty(ref _averageAmount, value);
        }

        public string TopCategory
        {
            get => _topCategory;
            set => SetProperty(ref _topCategory, value);
        }

        public double MonthlyGrowth
        {
            get => _monthlyGrowth;
            set => SetProperty(ref _monthlyGrowth, value);
        }

        public bool MonthlyGrowthPositive
        {
            get => _monthlyGrowthPositive;
            set => SetProperty(ref _monthlyGrowthPositive, value);
        }

        public bool ShowReports
        {
            get => _showReports;
            set => SetProperty(ref _showReports, value);
        }

        public DateTime ReportStartDate
        {
            get => _reportStartDate;
            set => SetProperty(ref _reportStartDate, value);
        }

        public DateTime ReportEndDate
        {
            get => _reportEndDate;
            set => SetProperty(ref _reportEndDate, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        #endregion

        #region Commands

        public ICommand AddCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand ClearFiltersCommand { get; private set; }

        // Report Commands
        public ICommand ToggleReportsCommand { get; private set; }
        public ICommand SetCurrentMonthCommand { get; private set; }
        public ICommand SetLastMonthCommand { get; private set; }
        public ICommand SetCurrentYearCommand { get; private set; }
        public ICommand PrintSummaryReportCommand { get; private set; }
        public ICommand PrintDetailedReportCommand { get; private set; }
        public ICommand PrintCategoryReportCommand { get; private set; }
        public ICommand PrintTrendReportCommand { get; private set; }
        public ICommand ExportToExcelCommand { get; private set; }

        private void InitializeCommands()
        {
            AddCommand = new RelayCommand(ExecuteAdd);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteEdit);
            DeleteCommand = new AsyncRelayCommand(ExecuteDeleteAsync, CanExecuteDelete);
            SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, CanExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            ClearFiltersCommand = new RelayCommand(ExecuteClearFilters);

            // Report Commands
            ToggleReportsCommand = new RelayCommand(ExecuteToggleReports);
            SetCurrentMonthCommand = new RelayCommand(ExecuteSetCurrentMonth);
            SetLastMonthCommand = new RelayCommand(ExecuteSetLastMonth);
            SetCurrentYearCommand = new RelayCommand(ExecuteSetCurrentYear);
            PrintSummaryReportCommand = new AsyncRelayCommand(ExecutePrintSummaryReportAsync);
            PrintDetailedReportCommand = new AsyncRelayCommand(ExecutePrintDetailedReportAsync);
            PrintCategoryReportCommand = new AsyncRelayCommand(ExecutePrintCategoryReportAsync);
            PrintTrendReportCommand = new AsyncRelayCommand(ExecutePrintTrendReportAsync);
            ExportToExcelCommand = new AsyncRelayCommand(ExecuteExportToExcelAsync);
        }

        #endregion

        #region Basic Command Methods

        private void ExecuteAdd(object parameter)
        {
            CurrentExpense = new ExpenseDTO
            {
                Date = DateTime.Today,
                Category = Categories.FirstOrDefault(c => c != "All") ?? "General"
            };
            IsEditMode = true;
        }

        private void ExecuteEdit(object parameter)
        {
            if (SelectedExpense != null)
            {
                CurrentExpense = new ExpenseDTO
                {
                    ExpenseId = SelectedExpense.ExpenseId,
                    Reason = SelectedExpense.Reason,
                    Amount = SelectedExpense.Amount,
                    Date = SelectedExpense.Date,
                    Notes = SelectedExpense.Notes,
                    Category = SelectedExpense.Category,
                    IsRecurring = SelectedExpense.IsRecurring,
                    CreatedAt = SelectedExpense.CreatedAt,
                    UpdatedAt = SelectedExpense.UpdatedAt
                };
                IsEditMode = true;
            }
        }

        private async Task ExecuteDeleteAsync(object parameter)
        {
            if (SelectedExpense == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the expense '{SelectedExpense.Reason}'?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    IsLoading = true;
                    LoadingMessage = "Deleting expense...";

                    await _expenseService.DeleteAsync(SelectedExpense.ExpenseId);
                    await LoadDataAsync();

                    MessageBox.Show("Expense deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting expense: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private async Task ExecuteSaveAsync(object parameter)
        {
            if (!ValidateCurrentExpense()) return;

            try
            {
                IsLoading = true;
                LoadingMessage = CurrentExpense.ExpenseId == 0 ? "Creating expense..." : "Updating expense...";

                if (CurrentExpense.ExpenseId == 0)
                {
                    await _expenseService.CreateAsync(CurrentExpense);
                    MessageBox.Show("Expense created successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _expenseService.UpdateAsync(CurrentExpense);
                    MessageBox.Show("Expense updated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await LoadDataAsync();
                IsEditMode = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving expense: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecuteCancel(object parameter)
        {
            IsEditMode = false;
            CurrentExpense = new ExpenseDTO { Date = DateTime.Today };
        }

        private void ExecuteClearFilters(object parameter)
        {
            SearchText = string.Empty;
            SelectedCategory = "All";
            StartDate = DateTime.Today.AddDays(-30);
            EndDate = DateTime.Today;
        }

        private bool CanExecuteEdit(object parameter) => SelectedExpense != null && !IsLoading;
        private bool CanExecuteDelete(object parameter) => SelectedExpense != null && !IsLoading;
        private bool CanExecuteSave(object parameter) => !IsLoading && ValidateCurrentExpense();

        #endregion

        #region Report Command Methods

        private void ExecuteToggleReports(object parameter)
        {
            ShowReports = !ShowReports;
        }

        private void ExecuteSetCurrentMonth(object parameter)
        {
            var now = DateTime.Today;
            ReportStartDate = new DateTime(now.Year, now.Month, 1);
            ReportEndDate = ReportStartDate.AddMonths(1).AddDays(-1);
        }

        private void ExecuteSetLastMonth(object parameter)
        {
            var now = DateTime.Today;
            var lastMonth = now.AddMonths(-1);
            ReportStartDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
            ReportEndDate = ReportStartDate.AddMonths(1).AddDays(-1);
        }

        private void ExecuteSetCurrentYear(object parameter)
        {
            var now = DateTime.Today;
            ReportStartDate = new DateTime(now.Year, 1, 1);
            ReportEndDate = new DateTime(now.Year, 12, 31);
        }

        private async Task ExecutePrintSummaryReportAsync(object parameter)
        {
            await PrintReport("Summary");
        }

        private async Task ExecutePrintDetailedReportAsync(object parameter)
        {
            await PrintReport("Detailed");
        }

        private async Task ExecutePrintCategoryReportAsync(object parameter)
        {
            await PrintReport("Category");
        }

        private async Task ExecutePrintTrendReportAsync(object parameter)
        {
            await PrintReport("Trend");
        }

        private async Task ExecuteExportToExcelAsync(object parameter)
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Exporting to Excel...";

                // This would integrate with an Excel export service
                MessageBox.Show("Excel export feature will be implemented in a future update.", "Coming Soon",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Printing Methods

        private async Task PrintReport(string reportType)
        {
            try
            {
                IsLoading = true;
                LoadingMessage = $"Preparing {reportType.ToLower()} report...";

                // Get expenses for the report period
                var reportExpenses = await _expenseService.GetByDateRangeAsync(ReportStartDate, ReportEndDate);

                if (!reportExpenses.Any())
                {
                    MessageBox.Show($"No expenses found for the selected date range ({ReportStartDate:MMM dd, yyyy} - {ReportEndDate:MMM dd, yyyy}).",
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Check printer availability
                bool printerAvailable = false;
                await Task.Run(() =>
                {
                    try
                    {
                        PrintServer printServer = new PrintServer();
                        PrintQueueCollection printQueues = printServer.GetPrintQueues();
                        printerAvailable = printQueues.Any();
                    }
                    catch (Exception)
                    {
                        printerAvailable = false;
                    }
                });

                if (!printerAvailable)
                {
                    MessageBox.Show("No printer available. Please connect a printer and try again.",
                        "Printer Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Show print dialog
                PrintDialog printDialog = new PrintDialog();
                bool? dialogResult = printDialog.ShowDialog();

                if (dialogResult != true)
                {
                    LoadingMessage = "Print cancelled";
                    return;
                }

                LoadingMessage = "Generating report document...";

                // Get business settings
                var companyName = await _businessSettingsService.GetSettingValueAsync("CompanyName", "Your Business Name");
                var address = await _businessSettingsService.GetSettingValueAsync("Address", "Your Business Address");
                var phoneNumber = await _businessSettingsService.GetSettingValueAsync("Phone", "Your Phone Number");
                var email = await _businessSettingsService.GetSettingValueAsync("Email", "");

                // Create the appropriate report document
                FlowDocument reportDocument = reportType switch
                {
                    "Summary" => CreateSummaryReport(printDialog, reportExpenses, companyName, address, phoneNumber, email),
                    "Detailed" => CreateDetailedReport(printDialog, reportExpenses, companyName, address, phoneNumber, email),
                    "Category" => CreateCategoryReport(printDialog, reportExpenses, companyName, address, phoneNumber, email),
                    "Trend" => CreateTrendReport(printDialog, reportExpenses, companyName, address, phoneNumber, email),
                    _ => CreateSummaryReport(printDialog, reportExpenses, companyName, address, phoneNumber, email)
                };

                LoadingMessage = "Printing report...";

                // Print the document
                printDialog.PrintDocument(
                    ((IDocumentPaginatorSource)reportDocument).DocumentPaginator,
                    $"Expense {reportType} Report");

                LoadingMessage = $"{reportType} report printed successfully";

                await Task.Delay(1500); // Show success message briefly
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error printing {reportType} report: {ex.Message}");
                MessageBox.Show($"Error printing {reportType.ToLower()} report: {ex.Message}", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Report Generation Methods

        private FlowDocument CreateSummaryReport(PrintDialog printDialog, IEnumerable<ExpenseDTO> expenses,
            string companyName, string address, string phoneNumber, string email)
        {
            var flowDocument = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = 12,
                PagePadding = new Thickness(40),
                TextAlignment = TextAlignment.Left
            };

            // Header
            var header = CreateReportHeader("EXPENSE SUMMARY REPORT", companyName, address, phoneNumber, email);
            flowDocument.Blocks.Add(header);

            // Report Period
            var periodParagraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            periodParagraph.Inlines.Add(new Run($"Report Period: {ReportStartDate:MMM dd, yyyy} - {ReportEndDate:MMM dd, yyyy}"));
            flowDocument.Blocks.Add(periodParagraph);

            flowDocument.Blocks.Add(CreateDivider());

            // Summary Statistics
            var expensesList = expenses.ToList();
            var totalAmount = expensesList.Sum(e => e.Amount);
            var totalCount = expensesList.Count;
            var averageAmount = totalCount > 0 ? totalAmount / totalCount : 0;

            var summaryTable = new Table { FontSize = 12, CellSpacing = 0, Margin = new Thickness(0, 0, 0, 20) };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            summaryTable.RowGroups.Add(new TableRowGroup());

            AddReportRow(summaryTable, "Total Expenses:", $"{totalAmount:C2}", true);
            AddReportRow(summaryTable, "Number of Transactions:", totalCount.ToString(), false);
            AddReportRow(summaryTable, "Average Amount:", $"{averageAmount:C2}", false);

            flowDocument.Blocks.Add(summaryTable);

            // Category Breakdown
            var categoryGroups = expensesList.GroupBy(e => e.Category)
                .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount), Count = g.Count() })
                .OrderByDescending(g => g.Amount)
                .ToList();

            if (categoryGroups.Any())
            {
                var categoryHeader = new Paragraph(new Run("EXPENSES BY CATEGORY"))
                {
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 20, 0, 10)
                };
                flowDocument.Blocks.Add(categoryHeader);

                var categoryTable = new Table { FontSize = 11, CellSpacing = 0 };
                categoryTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
                categoryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                categoryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                categoryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                categoryTable.RowGroups.Add(new TableRowGroup());

                // Header row
                var headerRow = new TableRow { Background = Brushes.LightGray };
                headerRow.Cells.Add(CreateCell("Category", FontWeights.Bold));
                headerRow.Cells.Add(CreateCell("Amount", FontWeights.Bold));
                headerRow.Cells.Add(CreateCell("Count", FontWeights.Bold));
                headerRow.Cells.Add(CreateCell("% of Total", FontWeights.Bold));
                categoryTable.RowGroups[0].Rows.Add(headerRow);

                foreach (var group in categoryGroups)
                {
                    var percentage = totalAmount > 0 ? (group.Amount / totalAmount) * 100 : 0;
                    var row = new TableRow();
                    row.Cells.Add(CreateCell(group.Category));
                    row.Cells.Add(CreateCell($"{group.Amount:C2}"));
                    row.Cells.Add(CreateCell(group.Count.ToString()));
                    row.Cells.Add(CreateCell($"{percentage:F1}%"));
                    categoryTable.RowGroups[0].Rows.Add(row);
                }

                flowDocument.Blocks.Add(categoryTable);
            }

            // Footer
            flowDocument.Blocks.Add(CreateReportFooter());

            return flowDocument;
        }

        private FlowDocument CreateDetailedReport(PrintDialog printDialog, IEnumerable<ExpenseDTO> expenses,
            string companyName, string address, string phoneNumber, string email)
        {
            var flowDocument = new FlowDocument
            {
                PageWidth = printDialog.PrintableAreaWidth,
                ColumnWidth = printDialog.PrintableAreaWidth,
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = 11,
                PagePadding = new Thickness(40),
                TextAlignment = TextAlignment.Left
            };

            // Header
            var header = CreateReportHeader("DETAILED EXPENSE REPORT", companyName, address, phoneNumber, email);
            flowDocument.Blocks.Add(header);

            // Report Period
            var periodParagraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            periodParagraph.Inlines.Add(new Run($"Report Period: {ReportStartDate:MMM dd, yyyy} - {ReportEndDate:MMM dd, yyyy}"));
            flowDocument.Blocks.Add(periodParagraph);

            flowDocument.Blocks.Add(CreateDivider());

            // Detailed Expenses Table
            var expensesList = expenses.OrderByDescending(e => e.Date).ToList();

            var detailTable = new Table { FontSize = 10, CellSpacing = 0 };
            detailTable.Columns.Add(new TableColumn { Width = new GridLength(80) });  // Date
            detailTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) }); // Reason
            detailTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) }); // Category
            detailTable.Columns.Add(new TableColumn { Width = new GridLength(80) }); // Amount
            detailTable.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) }); // Notes
            detailTable.RowGroups.Add(new TableRowGroup());

            // Header row
            var headerRow = new TableRow { Background = Brushes.LightGray };
            headerRow.Cells.Add(CreateCell("Date", FontWeights.Bold));
            headerRow.Cells.Add(CreateCell("Reason", FontWeights.Bold));
            headerRow.Cells.Add(CreateCell("Category", FontWeights.Bold));
            headerRow.Cells.Add(CreateCell("Amount", FontWeights.Bold));
            headerRow.Cells.Add(CreateCell("Notes", FontWeights.Bold));
            detailTable.RowGroups[0].Rows.Add(headerRow);

            decimal totalAmount = 0;
            foreach (var expense in expensesList)
            {
                var row = new TableRow();
                row.Cells.Add(CreateCell(expense.Date.ToString("MM/dd/yyyy")));
                row.Cells.Add(CreateCell(expense.Reason ?? ""));
                row.Cells.Add(CreateCell(expense.Category ?? ""));
                row.Cells.Add(CreateCell($"{expense.Amount:C2}"));
                row.Cells.Add(CreateCell(expense.Notes ?? ""));
                detailTable.RowGroups[0].Rows.Add(row);
                totalAmount += expense.Amount;
            }

            // Total row
            var totalRow = new TableRow { Background = Brushes.LightYellow };
            totalRow.Cells.Add(CreateCell("", FontWeights.Bold));
            totalRow.Cells.Add(CreateCell("TOTAL", FontWeights.Bold));
            totalRow.Cells.Add(CreateCell("", FontWeights.Bold));
            totalRow.Cells.Add(CreateCell($"{totalAmount:C2}", FontWeights.Bold));
            totalRow.Cells.Add(CreateCell("", FontWeights.Bold));
            detailTable.RowGroups[0].Rows.Add(totalRow);

            flowDocument.Blocks.Add(detailTable);

            // Footer
            flowDocument.Blocks.Add(CreateReportFooter());

            return flowDocument;
        }

        private FlowDocument CreateCategoryReport(PrintDialog printDialog, IEnumerable<ExpenseDTO> expenses,
            string companyName, string address, string phoneNumber, string email)
        {
            // Implementation for category analysis report
            return CreateSummaryReport(printDialog, expenses, companyName, address, phoneNumber, email);
        }

        private FlowDocument CreateTrendReport(PrintDialog printDialog, IEnumerable<ExpenseDTO> expenses,
            string companyName, string address, string phoneNumber, string email)
        {
            // Implementation for trend analysis report
            return CreateSummaryReport(printDialog, expenses, companyName, address, phoneNumber, email);
        }

        #endregion

        #region Helper Methods for Report Generation

        private Paragraph CreateReportHeader(string title, string companyName, string address, string phoneNumber, string email)
        {
            var header = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            header.Inlines.Add(new Run(companyName)
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });
            header.Inlines.Add(new LineBreak());

            if (!string.IsNullOrWhiteSpace(address))
            {
                header.Inlines.Add(new Run(address) { FontSize = 12 });
                header.Inlines.Add(new LineBreak());
            }

            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                header.Inlines.Add(new Run($"Phone: {phoneNumber}") { FontSize = 12 });
                if (!string.IsNullOrWhiteSpace(email))
                {
                    header.Inlines.Add(new Run(" | ") { FontSize = 12 });
                    header.Inlines.Add(new Run($"Email: {email}") { FontSize = 12 });
                }
                header.Inlines.Add(new LineBreak());
            }

            header.Inlines.Add(new LineBreak());
            header.Inlines.Add(new Run(title)
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });

            return header;
        }

        private BlockUIContainer CreateDivider()
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(0, 10, 0, 10)
            });
        }

        private Paragraph CreateReportFooter()
        {
            var footer = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0),
                FontSize = 10,
                Foreground = Brushes.Gray
            };

            footer.Inlines.Add(new Run($"Generated on {DateTime.Now:MMM dd, yyyy 'at' hh:mm tt}"));

            return footer;
        }

        private TableCell CreateCell(string text, FontWeight fontWeight = default)
        {
            var paragraph = new Paragraph(new Run(text ?? string.Empty))
            {
                FontWeight = fontWeight == default ? FontWeights.Normal : fontWeight,
                Margin = new Thickness(4, 2, 4, 2)
            };
            return new TableCell(paragraph);
        }

        private void AddReportRow(Table table, string label, string value, bool isBold = false)
        {
            var row = new TableRow();
            var fontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
            row.Cells.Add(CreateCell(label, fontWeight));
            row.Cells.Add(CreateCell(value, fontWeight));
            table.RowGroups[0].Rows.Add(row);
        }

        #endregion

        #region Data Loading and Analytics

        protected override async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;
                LoadingMessage = "Loading expenses...";

                var expensesTask = _expenseService.GetAllAsync();
                var categoriesTask = LoadCategoriesAsync();

                await Task.WhenAll(expensesTask, categoriesTask);

                _expenses = new ObservableCollection<ExpenseDTO>(
                    (await expensesTask).OrderByDescending(e => e.Date));

                ApplyFilters();
                await CalculateAnalytics();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var expenseCategories = await _categoryService.GetExpenseCategoriesAsync();

                Categories.Clear();
                Categories.Add("All");

                foreach (var category in expenseCategories.Where(c => c.IsActive))
                {
                    Categories.Add(category.Name);
                }

                if (!Categories.Contains("General"))
                    Categories.Add("General");
            }
            catch (Exception)
            {
                // Fallback categories
                Categories.Clear();
                var fallbackCategories = new[] { "All", "General", "Office Supplies", "Marketing", "Travel", "Utilities", "Equipment", "Rent", "Insurance" };
                foreach (var category in fallbackCategories)
                {
                    Categories.Add(category);
                }
            }
        }

        private async Task CalculateAnalytics()
        {
            try
            {
                // Calculate top category
                if (_expenses.Any())
                {
                    var categoryTotals = _expenses
                        .GroupBy(e => e.Category)
                        .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount) })
                        .OrderByDescending(x => x.Total)
                        .FirstOrDefault();

                    TopCategory = categoryTotals?.Category ?? "N/A";

                    // Calculate monthly growth
                    var currentMonth = DateTime.Today;
                    var lastMonth = currentMonth.AddMonths(-1);

                    var currentMonthTotal = _expenses
                        .Where(e => e.Date.Year == currentMonth.Year && e.Date.Month == currentMonth.Month)
                        .Sum(e => e.Amount);

                    var lastMonthTotal = _expenses
                        .Where(e => e.Date.Year == lastMonth.Year && e.Date.Month == lastMonth.Month)
                        .Sum(e => e.Amount);

                    if (lastMonthTotal > 0)
                    {
                        MonthlyGrowth = (double)((currentMonthTotal - lastMonthTotal) / lastMonthTotal);
                        MonthlyGrowthPositive = MonthlyGrowth > 0;
                    }
                    else
                    {
                        MonthlyGrowth = 0;
                        MonthlyGrowthPositive = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error calculating analytics: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            if (_expenses == null) return;

            var filtered = _expenses.Where(e =>
            {
                // Date filter
                if (e.Date < StartDate || e.Date > EndDate)
                    return false;

                // Category filter
                if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All" && e.Category != SelectedCategory)
                    return false;

                // Search filter
                if (!string.IsNullOrEmpty(SearchText))
                {
                    var searchLower = SearchText.ToLower();
                    var reasonMatch = e.Reason?.ToLower().Contains(searchLower) ?? false;
                    var categoryMatch = e.Category?.ToLower().Contains(searchLower) ?? false;
                    var notesMatch = !string.IsNullOrEmpty(e.Notes) && e.Notes.ToLower().Contains(searchLower);

                    if (!reasonMatch && !categoryMatch && !notesMatch)
                        return false;
                }

                return true;
            }).ToList();

            Expenses = new ObservableCollection<ExpenseDTO>(filtered);

            TotalAmount = filtered.Sum(e => e.Amount);
            TotalCount = filtered.Count;
            AverageAmount = TotalCount > 0 ? TotalAmount / TotalCount : 0;
        }

        private bool ValidateCurrentExpense()
        {
            if (CurrentExpense == null) return false;
            if (string.IsNullOrWhiteSpace(CurrentExpense.Reason)) return false;
            if (CurrentExpense.Amount <= 0) return false;
            if (string.IsNullOrWhiteSpace(CurrentExpense.Category)) return false;

            return true;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Expenses?.Clear();
                Categories?.Clear();
            }
            base.Dispose(disposing);
        }
    }
}