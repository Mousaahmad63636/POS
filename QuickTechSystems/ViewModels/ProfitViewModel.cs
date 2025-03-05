// Path: QuickTechSystems.WPF/ViewModels/ProfitViewModel.cs

using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ProfitViewModel : ViewModelBase
    {
        private readonly ITransactionService _transactionService;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _selectedDateRange;
        private decimal _grossProfit;
        private decimal _netProfit;
        private decimal _totalSales;
        private int _totalTransactions;
        private decimal _grossProfitPercentage;
        private decimal _netProfitPercentage;
        private ObservableCollection<ProfitDetailDTO> _profitDetails;

        public ProfitViewModel(
            ITransactionService transactionService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _transactionService = transactionService;
            _profitDetails = new ObservableCollection<ProfitDetailDTO>();
            _startDate = DateTime.Today.AddDays(-30);
            _endDate = DateTime.Today;
            _selectedDateRange = "Last 30 Days"; // Set default selection

            ExportCommand = new AsyncRelayCommand(async _ => await ExportReport());
            _ = LoadDataAsync();
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    _ = LoadDataAsync();
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
                    _ = LoadDataAsync();
                }
            }
        }

        public ObservableCollection<string> DateRanges { get; } = new()
        {
            "Today",
            "Yesterday",
            "Last 7 Days",
            "Last 30 Days",
            "This Month",
            "Last Month",
            "This Year"
        };

        public string SelectedDateRange
        {
            get => _selectedDateRange;
            set
            {
                if (SetProperty(ref _selectedDateRange, value))
                {
                    UpdateDateRange(value);
                }
            }
        }

        public decimal GrossProfit
        {
            get => _grossProfit;
            set => SetProperty(ref _grossProfit, value);
        }

        public decimal NetProfit
        {
            get => _netProfit;
            set => SetProperty(ref _netProfit, value);
        }

        public decimal TotalSales
        {
            get => _totalSales;
            set => SetProperty(ref _totalSales, value);
        }

        public int TotalTransactions
        {
            get => _totalTransactions;
            set => SetProperty(ref _totalTransactions, value);
        }

        public decimal GrossProfitPercentage
        {
            get => _grossProfitPercentage;
            set => SetProperty(ref _grossProfitPercentage, value);
        }

        public decimal NetProfitPercentage
        {
            get => _netProfitPercentage;
            set => SetProperty(ref _netProfitPercentage, value);
        }

        public ObservableCollection<ProfitDetailDTO> ProfitDetails
        {
            get => _profitDetails;
            set => SetProperty(ref _profitDetails, value);
        }

        public ICommand ExportCommand { get; }

        protected override async Task LoadDataAsync()
        {
            try
            {
                var transactions = await _transactionService.GetByDateRangeAsync(StartDate, EndDate);
                var transactionsList = transactions.ToList(); // Materialize the enumerable

                // Calculate profits
                TotalSales = transactionsList.Sum(t => t.TotalAmount);
                TotalTransactions = transactionsList.Count;

                GrossProfit = transactionsList.Sum(t =>
                    t.Details?.Sum(d => (d.UnitPrice - d.PurchasePrice) * d.Quantity) ?? 0);

                // Assuming 20% overhead costs for net profit calculation
                NetProfit = GrossProfit * 0.8m;

                GrossProfitPercentage = TotalSales > 0 ? (GrossProfit / TotalSales) * 100 : 0;
                NetProfitPercentage = TotalSales > 0 ? (NetProfit / TotalSales) * 100 : 0;

                // Group by date for details
                var details = transactionsList
                    .GroupBy(t => t.TransactionDate.Date)
                    .Select(g => new ProfitDetailDTO
                    {
                        Date = g.Key,
                        Sales = g.Sum(t => t.TotalAmount),
                        Cost = g.Sum(t => t.Details?.Sum(d => d.PurchasePrice * d.Quantity) ?? 0),
                        TransactionCount = g.Count()
                    })
                    .OrderByDescending(d => d.Date)
                    .ToList();

                ProfitDetails = new ObservableCollection<ProfitDetailDTO>(details);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading profit data: {ex.Message}");
            }
        }

        private void UpdateDateRange(string range)
        {
            switch (range)
            {
                case "Today":
                    StartDate = DateTime.Today;
                    EndDate = DateTime.Today;
                    break;
                case "Yesterday":
                    StartDate = DateTime.Today.AddDays(-1);
                    EndDate = DateTime.Today.AddDays(-1);
                    break;
                case "Last 7 Days":
                    StartDate = DateTime.Today.AddDays(-7);
                    EndDate = DateTime.Today;
                    break;
                case "Last 30 Days":
                    StartDate = DateTime.Today.AddDays(-30);
                    EndDate = DateTime.Today;
                    break;
                case "This Month":
                    StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    EndDate = DateTime.Today;
                    break;
                case "Last Month":
                    StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
                    EndDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);
                    break;
                case "This Year":
                    StartDate = new DateTime(DateTime.Today.Year, 1, 1);
                    EndDate = DateTime.Today;
                    break;
            }
        }

        private async Task ExportReport()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"Profit_Report_{DateTime.Now:yyyyMMdd}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var csv = new StringBuilder();
                    csv.AppendLine("Date,Sales,Cost,Gross Profit,Profit Margin %,Transactions");

                    foreach (var detail in ProfitDetails)
                    {
                        csv.AppendLine($"{detail.Date:d}," +
                            $"{detail.Sales:F2}," +
                            $"{detail.Cost:F2}," +
                            $"{detail.GrossProfit:F2}," +
                            $"{detail.ProfitMargin:P1}," +
                            $"{detail.TransactionCount}");
                    }

                    await File.WriteAllTextAsync(saveFileDialog.FileName, csv.ToString());
                    MessageBox.Show("Report exported successfully.", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error exporting report: {ex.Message}");
            }
        }
    }
}