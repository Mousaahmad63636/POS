// Path: QuickTechSystems.WPF/Views/TransactionWindow.xaml.cs
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

            // Call the public method instead
            _viewModel.InitializeDataAsync().ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"TransactionWindow created with ViewModel: {_viewModel.GetHashCode()}");
        }
    }
}