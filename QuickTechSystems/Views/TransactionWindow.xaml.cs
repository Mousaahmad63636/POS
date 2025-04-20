using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionWindow : Window
    {
        private readonly TransactionViewModel _viewModel;
        public string TableIdentifier { get; private set; }

        public TransactionWindow(string tableIdentifier)
        {
            InitializeComponent();

            // Create a completely new scope for this window's services
            var scope = ((App)App.Current).ServiceProvider.CreateScope();

            // Get a new TransactionViewModel instance from the scope
            _viewModel = scope.ServiceProvider.GetRequiredService<TransactionViewModel>();

            // Set the DataContext for the view
            transactionView.DataContext = _viewModel;

            // Set the table identifier and update window title
            TableIdentifier = tableIdentifier;
            this.Title = $"Transaction - Table {TableIdentifier}";

            // Set transaction reference in the ViewModel
            _viewModel.TableReference = tableIdentifier;

            // Store the scope for cleanup
            this.Tag = scope;

            // Handle window closing to clean up resources
            this.Closed += TransactionWindow_Closed;
        }

        private void TransactionWindow_Closed(object sender, EventArgs e)
        {
            // Cleanup resources
            if (_viewModel != null)
            {
                // Remove from manager
                TransactionWindowManager.Instance.RemoveWindow(this);

                // Dispose the ViewModel if it's an active transaction
                if (_viewModel.CurrentTransaction?.Details != null &&
                    _viewModel.CurrentTransaction.Details.Any())
                {
                    // Optionally prompt to save transaction
                    var result = MessageBox.Show(
                        $"Do you want to save the transaction for Table {TableIdentifier}?",
                        "Save Transaction",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Hold the transaction for later
                        _viewModel.HoldTransactionCommand.Execute(null);
                    }
                }

                // Dispose the ViewModel
                _viewModel.Dispose();

                // Dispose the service scope
                if (this.Tag is IServiceScope scope)
                {
                    scope.Dispose();
                }
            }
        }
    }
}