using System;
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionWindow : Window
    {
        private readonly TransactionViewModel _viewModel;

        public TransactionWindow()
        {
            InitializeComponent();

            // Set window properties
            Title = "Transaction";
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        public TransactionWindow(TransactionViewModel viewModel) : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            this.DataContext = _viewModel;

            // Initialize data
            _viewModel.InitializeDataAsync().ConfigureAwait(false);
        }
    }
}