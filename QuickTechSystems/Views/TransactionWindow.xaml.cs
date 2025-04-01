// Path: QuickTechSystems.WPF/Views/TransactionWindow.xaml.cs
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionWindow : Window
    {
        public TransactionWindow()
        {
            InitializeComponent();

            // Set default window properties
            Title = "Transaction";
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        public TransactionWindow(TransactionViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
}