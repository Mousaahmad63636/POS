// Path: QuickTechSystems.WPF/Views/MainTransactionView.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace QuickTechSystems.WPF.Views
{
    public partial class MainTransactionView : UserControl
    {
        private readonly IServiceProvider _serviceProvider;

        public MainTransactionView(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;

            // Create the first transaction tab
            AddNewTransactionTab();
        }

        private void AddNewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTransactionTab();
        }

        private void AddNewTransactionTab()
        {
            try
            {
                var viewModel = _serviceProvider.GetRequiredService<TransactionViewModel>();
                var transactionView = new TransactionView { DataContext = viewModel };

                var tabItem = new TabItem
                {
                    Header = $"Transaction {transactionTabControl.Items.Count + 1}",
                    Content = transactionView
                };

                transactionTabControl.Items.Add(tabItem);
                transactionTabControl.SelectedItem = tabItem;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error creating new transaction tab: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (transactionTabControl.SelectedItem is TabItem tabItem)
            {
                if (tabItem.Content is TransactionView view &&
                    view.DataContext is TransactionViewModel viewModel)
                {
                    if (viewModel.CurrentTransaction?.Details?.Count > 0)
                    {
                        var result = MessageBox.Show(
                            "This transaction has items. Are you sure you want to close it?",
                            "Confirm",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                            return;

                        // Dispose the view model
                        viewModel.Dispose();
                    }
                }

                transactionTabControl.Items.Remove(tabItem);

                // If all tabs are closed, add a new one
                if (transactionTabControl.Items.Count == 0)
                {
                    AddNewTransactionTab();
                }
            }
        }
    }
}