using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.ViewModels
{
    public class DrawerViewModel : ViewModelBase
    {
        private readonly IDrawerService _drawerService;
        private readonly IWindowService _windowService;
        private DrawerDTO? _currentDrawer;
        private ObservableCollection<DrawerTransactionDTO> _drawerHistory;
        private string _statusMessage = string.Empty;
        private bool _isProcessing;

        public DrawerViewModel(
            IDrawerService drawerService,
            IWindowService windowService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _drawerService = drawerService;
            _windowService = windowService;
            _drawerHistory = new ObservableCollection<DrawerTransactionDTO>();

            OpenDrawerCommand = new AsyncRelayCommand(async _ => await OpenDrawerAsync(), _ => CanOpenDrawer && !IsProcessing);
            AddCashCommand = new AsyncRelayCommand(async _ => await AddCashAsync(), _ => IsDrawerOpen && !IsProcessing);
            RemoveCashCommand = new AsyncRelayCommand(async _ => await RemoveCashAsync(), _ => IsDrawerOpen && !IsProcessing);
            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync(), _ => IsDrawerOpen && !IsProcessing);
            PrintReportCommand = new AsyncRelayCommand(async _ => await PrintReportAsync(), _ => IsDrawerOpen && !IsProcessing);

            _ = LoadDataAsync();
        }

        public DrawerDTO? CurrentDrawer
        {
            get => _currentDrawer;
            set
            {
                SetProperty(ref _currentDrawer, value);
                OnPropertyChanged(nameof(IsDrawerOpen));
                OnPropertyChanged(nameof(CanOpenDrawer));
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

        public bool IsDrawerOpen => CurrentDrawer?.Status == "Open";
        public bool CanOpenDrawer => CurrentDrawer == null || CurrentDrawer.Status == "Closed";

        public ICommand OpenDrawerCommand { get; }
        public ICommand AddCashCommand { get; }
        public ICommand RemoveCashCommand { get; }
        public ICommand CloseDrawerCommand { get; }
        public ICommand PrintReportCommand { get; }

        protected override async Task LoadDataAsync()
        {
            try
            {
                IsProcessing = true;
                CurrentDrawer = await _drawerService.GetCurrentDrawerAsync();
                await LoadDrawerHistoryAsync();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading drawer data: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task LoadDrawerHistoryAsync()
        {
            if (CurrentDrawer == null) return;

            try
            {
                var history = await _drawerService.GetDrawerHistoryAsync(CurrentDrawer.DrawerId);
                DrawerHistory = new ObservableCollection<DrawerTransactionDTO>(history);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading drawer history: {ex.Message}");
            }
        }

        private async Task OpenDrawerAsync()
        {
            try
            {
                IsProcessing = true;
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Open Drawer", "Enter opening balance:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    CurrentDrawer = await _drawerService.OpenDrawerAsync(
                        amount,
                        "default_user_001",
                        "Default Cashier"
                    );
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show("Drawer opened successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error opening drawer: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task AddCashAsync()
        {
            try
            {
                IsProcessing = true;
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Add Cash", "Enter amount to add:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    CurrentDrawer = await _drawerService.AddCashTransactionAsync(amount, true);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show($"Successfully added {amount:C2}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
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
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Remove Cash", "Enter amount to remove:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    if (amount > CurrentDrawer?.CurrentBalance)
                    {
                        MessageBox.Show("Amount exceeds current balance.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    CurrentDrawer = await _drawerService.AddCashTransactionAsync(amount, false);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show($"Successfully removed {amount:C2}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
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
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Close Drawer", "Enter final cash amount:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal finalAmount))
                {
                    string notes = await ShowNotesInputDialog();
                    CurrentDrawer = await _drawerService.CloseDrawerAsync(finalAmount, notes);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();

                    var difference = CurrentDrawer.Difference;
                    var message = difference == 0
                        ? "Drawer closed successfully with no discrepancy."
                        : $"Drawer closed with a {(difference > 0 ? "surplus" : "shortage")} of {Math.Abs(difference):C2}";

                    MessageBox.Show(message, "Drawer Closed",
                        MessageBoxButton.OK,
                        difference == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error closing drawer: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task PrintReportAsync()
        {
            try
            {
                IsProcessing = true;
                if (CurrentDrawer == null) return;

                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    var document = CreateDrawerReport();
                    printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Drawer Report");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error printing report: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void UpdateStatus()
        {
            if (CurrentDrawer == null)
            {
                StatusMessage = "No drawer is currently open";
                return;
            }

            var status = CurrentDrawer.Status == "Open" ? "open" : "closed";
            var time = CurrentDrawer.Status == "Open"
                ? $"since {CurrentDrawer.OpenedAt:t}"
                : $"at {CurrentDrawer.ClosedAt:t}";

            StatusMessage = $"Drawer is {status} - {CurrentDrawer.CashierName} - {time}";
        }

        private async Task<string> ShowNotesInputDialog()
        {
            var window = _windowService.GetCurrentWindow();
            var dialog = new InputDialog("Closing Notes", "Enter any notes for closing:")
            {
                Owner = window
            };

            return dialog.ShowDialog() == true ? dialog.Input : string.Empty;
        }

        private FlowDocument CreateDrawerReport()
        {
            var document = new FlowDocument();

            // Add report content (header, summary, transactions, etc.)
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Bold(new Run("Drawer Report\n")) { FontSize = 18 });
            paragraph.Inlines.Add(new Run($"Generated: {DateTime.Now:g}\n\n"));

            if (CurrentDrawer != null)
            {
                paragraph.Inlines.Add(new Bold(new Run("Summary:\n")));
                paragraph.Inlines.Add(new Run($"Opening Balance: {CurrentDrawer.OpeningBalance:C2}\n"));
                paragraph.Inlines.Add(new Run($"Current Balance: {CurrentDrawer.CurrentBalance:C2}\n"));
                paragraph.Inlines.Add(new Run($"Cash In: {CurrentDrawer.CashIn:C2}\n"));
                paragraph.Inlines.Add(new Run($"Cash Out: {CurrentDrawer.CashOut:C2}\n"));
                paragraph.Inlines.Add(new Run($"Difference: {CurrentDrawer.Difference:C2}\n\n"));
            }

            document.Blocks.Add(paragraph);

            // Add transaction history
            if (DrawerHistory.Any())
            {
                var table = new Table();

                // Create table columns
                var columns = new[] { 150.0, 100.0, 100.0, 100.0, 100.0 };
                foreach (var width in columns)
                {
                    table.Columns.Add(new TableColumn { Width = new GridLength(width) });
                }

                // Create header row
                var headerRow = new TableRow();
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Timestamp"))));
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Type"))));
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Amount"))));
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Balance"))));
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Notes"))));
                table.RowGroups.Add(new TableRowGroup());
                table.RowGroups[0].Rows.Add(headerRow);

                // Add transaction rows
                foreach (var transaction in DrawerHistory)
                {
                    var row = new TableRow();
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Timestamp.ToString("g")))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Type))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Amount.ToString("C2")))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Balance.ToString("C2")))));
                    row.Cells.Add(new TableCell(new Paragraph(new Run(transaction.Notes ?? string.Empty))));
                    table.RowGroups[0].Rows.Add(row);
                }

                document.Blocks.Add(table);
            }

            return document;
        }
    }
}