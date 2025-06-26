using System;
using System.Windows.Controls;
using QuickTechSystems.ViewModels.Transaction;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionHistoryView : UserControl
    {
        public TransactionHistoryView()
        {
            InitializeComponent();
        }

        private void TodayButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TransactionHistoryViewModel viewModel)
            {
                var today = DateTime.Today;
                viewModel.StartDate = today;
                viewModel.EndDate = today;
            }
        }

        private void ThisWeekButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TransactionHistoryViewModel viewModel)
            {
                var today = DateTime.Today;
                // Calculate start of week (Sunday)
                var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                var endOfWeek = startOfWeek.AddDays(6);

                viewModel.StartDate = startOfWeek;
                viewModel.EndDate = endOfWeek;
            }
        }

        private void ThisMonthButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TransactionHistoryViewModel viewModel)
            {
                var today = DateTime.Today;
                var startOfMonth = new DateTime(today.Year, today.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

                viewModel.StartDate = startOfMonth;
                viewModel.EndDate = endOfMonth;
            }
        }
    }
}