using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.Views
{
    public partial class TableTransactionPanel : UserControl, INotifyPropertyChanged
    {
        private DispatcherTimer _refreshTimer;
        public event PropertyChangedEventHandler PropertyChanged;

        public TableTransactionPanel()
        {
            InitializeComponent();

            // Setup timer to refresh open tables list
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            // Initial refresh
            RefreshOpenTablesList();

            // Handle unloaded to clean up resources
            this.Unloaded += TableTransactionPanel_Unloaded;
        }

        private void TableTransactionPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop timer when control is unloaded
            _refreshTimer.Stop();
            _refreshTimer = null;
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshOpenTablesList();
        }

        private void RefreshOpenTablesList()
        {
            // Get open tables from manager
            var openTables = TransactionWindowManager.Instance.OpenTableIds;

            // Update the ListView
            TablesListView.ItemsSource = null;
            TablesListView.ItemsSource = openTables;
        }

        private void NewTable_Click(object sender, RoutedEventArgs e)
        {
            // Use your existing InputDialog
            var dialog = new InputDialog("New Table Transaction", "Enter table number or leave blank for auto-assigned:");

            // Make sure to set Owner if needed
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                string tableId = dialog.Input?.Trim();

                // Create new transaction window with specified or auto-generated ID
                TransactionWindowManager.Instance.CreateNewTransactionWindow(tableId);

                // Refresh the list
                RefreshOpenTablesList();
            }
        }
        private void OpenTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tableId)
            {
                TransactionWindowManager.Instance.ActivateWindow(tableId);
            }
        }

        private void TablesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Clear selection immediately to maintain proper visual state
            TablesListView.SelectedItem = null;
        }
    }
}