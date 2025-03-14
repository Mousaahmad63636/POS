﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Infrastructure.Data;
using System.Diagnostics;
using System.Text;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Collections.ObjectModel;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.WPF.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class TransactionHistoryViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ICategoryService _categoryService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private CancellationTokenSource _cts;

        private ObservableCollection<TransactionDTO> _transactions;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<TransactionDTO> _filteredTransactions;
        private CategoryDTO? _selectedCategory;
        private decimal _totalSales;
        private decimal _totalProfit;
        private string _searchText = string.Empty;
        private bool _isRefreshing;
        private bool _isLoading;
        private string _errorMessage = string.Empty;
        private DateTime _startDate = DateTime.Today.AddDays(-30);
        private DateTime _endDate = DateTime.Today;
        private bool _isDateRangeValid = true;
        private decimal _totalReturns;
        private int _totalTransactions;
        private Dictionary<string, decimal> _categorySales = new();

        private Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;

        public ObservableCollection<TransactionDTO> Transactions
        {
            get => _transactions;
            private set => SetProperty(ref _transactions, value);
        }

        public ObservableCollection<TransactionDTO> FilteredTransactions
        {
            get => _filteredTransactions;
            private set => SetProperty(ref _filteredTransactions, value);
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            private set => SetProperty(ref _categories, value);
        }

        public CategoryDTO? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public decimal TotalSales
        {
            get => _totalSales;
            private set => SetProperty(ref _totalSales, value);
        }

        public decimal TotalProfit
        {
            get => _totalProfit;
            private set => SetProperty(ref _totalProfit, value);
        }

        public decimal TotalReturns
        {
            get => _totalReturns;
            private set => SetProperty(ref _totalReturns, value);
        }

        public int TotalTransactions
        {
            get => _totalTransactions;
            private set => SetProperty(ref _totalTransactions, value);
        }

        public Dictionary<string, decimal> CategorySales
        {
            get => _categorySales;
            private set => SetProperty(ref _categorySales, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    ValidateDateRange();
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    ValidateDateRange();
                    _ = SafeLoadDataAsync();
                }
            }
        }

        public bool IsDateRangeValid
        {
            get => _isDateRangeValid;
            private set => SetProperty(ref _isDateRangeValid, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilters();
                }
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set => SetProperty(ref _isRefreshing, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public ICommand ExportCommand { get; }
        public ICommand PrintReportCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewTransactionDetailsCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand DeleteTransactionCommand { get; }

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
            _filteredTransactions = new ObservableCollection<TransactionDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _transactionChangedHandler = HandleTransactionChanged;
            _cts = new CancellationTokenSource();

            ExportCommand = new AsyncRelayCommand(async _ => await ExportTransactionsAsync(), CanExecuteCommand);
            PrintReportCommand = new AsyncRelayCommand(async _ => await PrintTransactionReportAsync(), CanExecuteCommand);
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshDataAsync(), CanExecuteCommand);
            ViewTransactionDetailsCommand = new RelayCommand(ShowTransactionDetails, CanShowTransactionDetails);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            DeleteTransactionCommand = new AsyncRelayCommand<TransactionDTO>(
                async transaction => await DeleteTransactionAsync(transaction),
                CanDeleteTransaction);

            _ = InitializeAsync();
        }

        private void ValidateDateRange()
        {
            IsDateRangeValid = StartDate <= EndDate;
            ErrorMessage = !IsDateRangeValid ? "Start date must be before or equal to end date" : string.Empty;
        }

        private async Task InitializeAsync()
        {
            try
            {
                await LoadCategoriesAsync();
                await SafeLoadDataAsync();
            }
            catch (Exception ex)
            {
                HandleError("Initialization error", ex);
            }
        }
        private bool CanDeleteTransaction(TransactionDTO? transaction)
        {
            return transaction != null && !IsLoading && !IsRefreshing;
        }


        private async Task DeleteTransactionAsync(TransactionDTO? transaction)
        {
            if (transaction == null) return;

            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Another operation is in progress. Please try again in a moment.");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                // Confirm deletion
                var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    return MessageBox.Show(
                        GetOwnerWindow(),
                        $"Are you sure you want to delete transaction #{transaction.TransactionId}?\nThis action cannot be undone.",
                        "Confirm Deletion",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;
                });

                if (!result)
                {
                    return;
                }

                // Delete transaction
                bool success = await _transactionService.DeleteAsync(transaction.TransactionId);

                if (success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Remove from collections
                        if (Transactions.Contains(transaction))
                            Transactions.Remove(transaction);

                        if (FilteredTransactions.Contains(transaction))
                            FilteredTransactions.Remove(transaction);

                        // Recalculate totals
                        CalculateTotals();

                        // Show success message
                        MessageBox.Show(
                            GetOwnerWindow(),
                            $"Transaction #{transaction.TransactionId} has been deleted successfully.",
                            "Transaction Deleted",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }
                else
                {
                    await ShowErrorMessageAsync($"Failed to delete transaction #{transaction.TransactionId}");
                }
            }
            catch (Exception ex)
            {
                HandleError("Error deleting transaction", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }
        private async Task LoadCategoriesAsync()
        {
            if (!await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("LoadCategoriesAsync skipped - operation in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                try
                {
                    using var context = await _dbContextFactory.CreateDbContextAsync();
                    var categories = await context.Set<Category>()
                        .Where(c => c.IsActive)
                        .Select(c => new CategoryDTO
                        {
                            CategoryId = c.CategoryId,
                            Name = c.Name,
                            Description = c.Description,
                            IsActive = c.IsActive
                        })
                        .ToListAsync();

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories = new ObservableCollection<CategoryDTO>(
                            new[] { new CategoryDTO { CategoryId = 0, Name = "All Categories" } }
                            .Concat(categories)
                        );
                        SelectedCategory = Categories.First();
                    });
                }
                catch (Exception ex)
                {
                    HandleError("Error loading categories", ex);
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task SafeLoadDataAsync()
        {
            if (!IsDateRangeValid || !await _operationLock.WaitAsync(0))
            {
                Debug.WriteLine("SafeLoadDataAsync skipped - invalid date range or operation in progress");
                return;
            }

            // Create a new CancellationTokenSource for this operation
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                try
                {
                    // Add a timeout for the operation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    // Create a new context for this operation
                    using var context = await _dbContextFactory.CreateDbContextAsync();

                    var transactions = await _transactionService.GetByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var summary = await _transactionService.GetTransactionSummaryByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var categorySales = await _transactionService.GetCategorySalesByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    var transactionCount = await _transactionService.GetTransactionCountByDateRangeAsync(StartDate, EndDate);
                    if (linkedCts.Token.IsCancellationRequested) return;

                    if (SelectedCategory?.CategoryId > 0)
                    {
                        transactions = transactions.Where(t =>
                            t.Details.Any(d => d.CategoryId == SelectedCategory.CategoryId)).ToList();
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!linkedCts.Token.IsCancellationRequested)
                        {
                            Transactions = new ObservableCollection<TransactionDTO>(
                                transactions.OrderByDescending(t => t.TransactionDate)
                            );
                            TotalSales = summary.TotalSales;
                            TotalReturns = summary.TotalReturns;
                            TotalTransactions = transactionCount;
                            CategorySales = new Dictionary<string, decimal>(categorySales);

                            ApplyFilters();
                            CalculateTotals();
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation was canceled");
                }
                catch (Exception ex)
                {
                    HandleError("Error loading transactions", ex);
                }
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private void ApplyFilters()
        {
            try
            {
                var filtered = Transactions.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    filtered = filtered.Where(t =>
                        (t.CustomerName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        t.TransactionId.ToString().Contains(SearchText) ||
                        (t.Details?.Any(d => d.ProductName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ?? false) ||
                        t.CashierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
                }

                FilteredTransactions = new ObservableCollection<TransactionDTO>(filtered);
                CalculateTotals();
            }
            catch (Exception ex)
            {
                HandleError("Error applying filters", ex);
            }
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedCategory = Categories.First();
            StartDate = DateTime.Today.AddDays(-30);
            EndDate = DateTime.Today;
            ApplyFilters();
        }

        private void CalculateTotals()
        {
            try
            {
                TotalSales = FilteredTransactions.Sum(t => t.TotalAmount);
                TotalProfit = FilteredTransactions.Sum(t =>
                    t.Details?.Sum(d => (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0);
            }
            catch (Exception ex)
            {
                HandleError("Error calculating totals", ex);
            }
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                IsRefreshing = true;
                await SafeLoadDataAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private bool CanExecuteCommand(object? parameter)
        {
            return !IsLoading && !IsRefreshing &&
                   FilteredTransactions.Any() &&
                   IsDateRangeValid;
        }

        private bool CanShowTransactionDetails(object? parameter)
        {
            return parameter is TransactionDTO;
        }

        private void ShowTransactionDetails(object? parameter)
        {
            if (parameter is not TransactionDTO transaction) return;

            try
            {
                var ownerWindow = GetOwnerWindow();
                var detailWindow = new TransactionDetailWindow(transaction)
                {
                    Owner = ownerWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                detailWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                HandleError("Error showing transaction details", ex);
            }
        }

        private Window GetOwnerWindow()
        {
            // Try to get the active window first
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            // Fall back to the main window
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            // Last resort, get any window that's visible
            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        // Prevent excessive event handling with throttling
        private DateTime _lastTransactionChangedTime = DateTime.MinValue;
        private object _eventLock = new object();

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            // Skip if loading or refreshing
            if (IsLoading || IsRefreshing) return;

            // Throttle events
            lock (_eventLock)
            {
                var now = DateTime.Now;
                if ((now - _lastTransactionChangedTime).TotalMilliseconds < 500)
                {
                    return; // Ignore events that come too quickly
                }
                _lastTransactionChangedTime = now;
            }

            try
            {
                await Task.Delay(200); // Small delay to group multiple quick changes
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await SafeLoadDataAsync();
                });
            }
            catch (Exception ex)
            {
                HandleError("Error handling transaction change", ex);
            }
        }

        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"{message}: {ex}");

            // Handle specific database errors
            if (ex.Message.Contains("A second operation was started") ||
                (ex.InnerException != null && ex.InnerException.Message.Contains("A second operation was started")))
            {
                ErrorMessage = "The system is processing another request. Please try again in a moment.";
            }
            else if (ex.Message.Contains("The connection was closed") ||
                    (ex.InnerException != null && ex.InnerException.Message.Contains("The connection was closed")))
            {
                ErrorMessage = "Database connection lost. Please check your connection and try again.";
            }
            else
            {
                ErrorMessage = $"{message}: {ex.Message}";
            }

            // Clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage.Contains(ex.Message))
                    {
                        ErrorMessage = string.Empty;
                    }
                });
            });

            // If this is a critical error that shows a dialog, ensure it has a proper owner
            if (message.Contains("critical", StringComparison.OrdinalIgnoreCase))
            {
                var ownerWindow = GetOwnerWindow();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        ownerWindow,
                        $"{message}: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            if (!_isDisposed)
            {
                _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _operationLock.Dispose();
                    UnsubscribeFromEvents();
                }
                _isDisposed = true;
            }
        }

        private async Task ExportTransactionsAsync()
        {
            if (!FilteredTransactions.Any())
            {
                await ShowErrorMessageAsync("No transactions to export");
                return;
            }

            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Export already in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"Transaction_History_{DateTime.Now:yyyyMMdd}"
                };

                // Use InvokeAsync to show the dialog on the UI thread
                bool? result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    saveFileDialog.ShowDialog());

                if (result == true)
                {
                    var csv = new StringBuilder();
                    csv.AppendLine("Transaction ID,Date,Customer,Type,Items,Total Amount,Profit,Status,Cashier,Category");

                    foreach (var transaction in FilteredTransactions)
                    {
                        var profit = transaction.Details?.Sum(d =>
                            (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0;

                        var itemCount = transaction.Details?.Count ?? 0;
                        var categories = string.Join(";", transaction.Details?
                            .Select(d => d.CategoryId)
                            .Distinct()
                            .Select(id => Categories.FirstOrDefault(c => c.CategoryId == id)?.Name ?? "Unknown")
                            ?? Array.Empty<string>());

                        csv.AppendLine($"{transaction.TransactionId}," +
                            $"\"{transaction.TransactionDate:g}\"," +
                            $"\"{transaction.CustomerName}\"," +
                            $"{transaction.TransactionType}," +
                            $"{itemCount}," +
                            $"{transaction.TotalAmount:F2}," +
                            $"{profit:F2}," +
                            $"{transaction.Status}," +
                            $"\"{transaction.CashierName}\"," +
                            $"\"{categories}\"");

                        // Add detailed items if present
                        if (transaction.Details != null)
                        {
                            foreach (var detail in transaction.Details)
                            {
                                csv.AppendLine($"," +
                                    $"," +
                                    $"," +
                                    $"Item Detail," +
                                    $"\"{detail.ProductName}\"," +
                                    $"{detail.Quantity}," +
                                    $"{detail.UnitPrice:F2}," +
                                    $"{detail.Total:F2}");
                            }
                        }
                    }

                    await File.WriteAllTextAsync(saveFileDialog.FileName, csv.ToString());
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var ownerWindow = GetOwnerWindow();
                        MessageBox.Show(
                            ownerWindow,
                            "Transactions exported successfully.",
                            "Export Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                HandleError("Error exporting transactions", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task PrintTransactionReportAsync()
        {
            if (!FilteredTransactions.Any())
            {
                await ShowErrorMessageAsync("No transactions to print");
                return;
            }

            if (!await _operationLock.WaitAsync(0))
            {
                await ShowErrorMessageAsync("Print operation already in progress");
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                // Get the owner window
                var ownerWindow = GetOwnerWindow();

                // Create print dialog without trying to set the Owner property
                var printDialog = new PrintDialog();

                // Setting the Owner through ShowDialog on PrintDialog isn't supported
                // Instead, we'll make sure we're on the UI thread when showing the dialog
                if (await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => printDialog.ShowDialog() == true))
                {
                    var document = new FlowDocument
                    {
                        PagePadding = new Thickness(50),
                        FontFamily = new FontFamily("Arial")
                    };

                    // Report Header
                    var reportHeader = new Paragraph(new Run("Transaction History Report"))
                    {
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 20)
                    };
                    document.Blocks.Add(reportHeader);

                    // Date Range
                    var dateRange = new Paragraph
                    {
                        Margin = new Thickness(0, 0, 0, 20)
                    };
                    dateRange.Inlines.Add(new Run($"Period: {StartDate:d} to {EndDate:d}\n"));
                    document.Blocks.Add(dateRange);

                    // Summary Section
                    var summary = new Paragraph
                    {
                        Margin = new Thickness(0, 0, 0, 20)
                    };
                    summary.Inlines.Add(new Bold(new Run("Summary\n")));
                    summary.Inlines.Add(new Run($"Total Transactions: {FilteredTransactions.Count}\n"));
                    summary.Inlines.Add(new Run($"Total Sales: {TotalSales:C}\n"));
                    summary.Inlines.Add(new Run($"Total Profit: {TotalProfit:C}\n"));
                    summary.Inlines.Add(new Run($"Total Returns: {TotalReturns:C}\n"));
                    document.Blocks.Add(summary);

                    // Category Summary
                    if (CategorySales.Any())
                    {
                        var categorySummary = new Paragraph
                        {
                            Margin = new Thickness(0, 0, 0, 20)
                        };
                        categorySummary.Inlines.Add(new Bold(new Run("Sales by Category\n")));
                        foreach (var category in CategorySales.OrderByDescending(x => x.Value))
                        {
                            categorySummary.Inlines.Add(new Run($"{category.Key}: {category.Value:C}\n"));
                        }
                        document.Blocks.Add(categorySummary);
                    }

                    // Transactions Table
                    var table = new Table { CellSpacing = 0 };

                    // Define columns
                    var columnWidths = new[] { 80.0, 120.0, 150.0, 100.0, 100.0, 120.0 };
                    foreach (var width in columnWidths)
                    {
                        table.Columns.Add(new TableColumn { Width = new GridLength(width) });
                    }

                    // Add header row
                    var tableHeaderRow = new TableRow { Background = Brushes.LightGray };
                    foreach (var columnHeader in new[] { "ID", "Date", "Customer", "Total", "Status", "Cashier" })
                    {
                        tableHeaderRow.Cells.Add(new TableCell(new Paragraph(new Bold(new Run(columnHeader)))));
                    }

                    var rowGroup = new TableRowGroup();
                    rowGroup.Rows.Add(tableHeaderRow);

                    // Add transaction rows
                    foreach (var transaction in FilteredTransactions)
                    {
                        var row = new TableRow();

                        // Add cells
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TransactionId.ToString()))));
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TransactionDate.ToString("g")))));
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.CustomerName ?? ""))));
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.TotalAmount.ToString("C")))));
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Status.ToString()))));
                        row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.CashierName))));

                        rowGroup.Rows.Add(row);

                        // Add transaction details if present
                        if (transaction.Details?.Any() == true)
                        {
                            var detailsRow = new TableRow();
                            var detailsCell = new TableCell { ColumnSpan = 6 };
                            var detailsParagraph = new Paragraph { Margin = new Thickness(20, 5, 5, 5) };

                            foreach (var detail in transaction.Details)
                            {
                                detailsParagraph.Inlines.Add(new Run(
                                    $"• {detail.ProductName} - Qty: {detail.Quantity} @ {detail.UnitPrice:C} = {detail.Total:C}\n"));
                            }

                            detailsCell.Blocks.Add(detailsParagraph);
                            detailsRow.Cells.Add(detailsCell);
                            rowGroup.Rows.Add(detailsRow);
                        }
                    }

                    table.RowGroups.Add(rowGroup);
                    document.Blocks.Add(table);

                    // Footer
                    var footer = new Paragraph(new Run($"Generated: {DateTime.Now:g}"))
                    {
                        FontStyle = FontStyles.Italic,
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 20, 0, 0)
                    };
                    document.Blocks.Add(footer);

                    // Print the document
                    printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,
                        "Transaction History Report");
                }
            }
            catch (Exception ex)
            {
                HandleError("Error printing report", ex);
            }
            finally
            {
                IsLoading = false;
                _operationLock.Release();
            }
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            ErrorMessage = message;

            var ownerWindow = GetOwnerWindow();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    ownerWindow,
                    message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(3000); // Show error for 3 seconds
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message) // Only clear if still the same message
                    {
                        ErrorMessage = string.Empty;
                    }
                });
            });
        }
    }
}