// Path: QuickTechSystems.WPF.Views/BulkMainStockDialog.xaml.cs
using System;
using System.ComponentModel;
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class BulkMainStockDialog : Window
    {
        public BulkMainStockDialog()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Check if we're saving
            if (DataContext is BulkMainStockViewModel viewModel && viewModel.IsSaving)
            {
                var result = MessageBox.Show("A save operation is in progress. Are you sure you want to cancel?",
                    "Cancel Operation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // Set up a binding to automatically close the dialog when the ViewModel sets DialogResult
            if (DataContext is BulkMainStockViewModel viewModel)
            {
                viewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(BulkMainStockViewModel.DialogResult) &&
                        viewModel.DialogResult.HasValue)
                    {
                        this.DialogResult = viewModel.DialogResult.Value;
                    }
                };
            }
        }
    }
}