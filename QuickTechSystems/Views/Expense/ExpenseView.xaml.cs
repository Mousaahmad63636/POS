using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.ViewModels.Expense;

namespace QuickTechSystems.WPF.Views
{
    public partial class ExpenseView : UserControl
    {
        private ExpenseWindow? _expenseWindow;

        public ExpenseView()
        {
            InitializeComponent();
            Loaded += ExpenseView_Loaded;
            SizeChanged += OnControlSizeChanged;
        }

        private void ExpenseView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            AdjustLayoutForSize();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExpenseViewModel.IsExpensePopupOpen) &&
                DataContext is ExpenseViewModel viewModel && viewModel.IsExpensePopupOpen)
            {
                OpenExpenseWindow(viewModel);
            }
        }

        private void OpenExpenseWindow(ExpenseViewModel viewModel)
        {
            _expenseWindow?.Close();

            _expenseWindow = new ExpenseWindow(viewModel);
            _expenseWindow.Owner = Window.GetWindow(this);
            _expenseWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _expenseWindow.Closed += (s, e) =>
            {
                viewModel.IsExpensePopupOpen = false;
                _expenseWindow = null;
            };

            _expenseWindow.Show();
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustLayoutForSize();
        }

        private void AdjustLayoutForSize()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            double width = window.ActualWidth;

            // Adjust margins based on screen size
            var rootGrid = Content as Grid;
            if (rootGrid?.Children[0] is ScrollViewer scrollViewer &&
                scrollViewer.Content is Grid contentGrid)
            {
                contentGrid.Margin = width switch
                {
                    >= 1920 => new Thickness(32),
                    >= 1366 => new Thickness(24),
                    >= 800 => new Thickness(16),
                    _ => new Thickness(8)
                };
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid { SelectedItem: ExpenseDTO expense } &&
                DataContext is ExpenseViewModel viewModel)
            {
                viewModel.EditCommand.Execute(expense);
            }
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel && viewModel.SelectedExpense != null)
            {
                viewModel.EditCommand.Execute(viewModel.SelectedExpense);
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel && viewModel.SelectedExpense != null)
            {
                viewModel.DeleteCommand.Execute(viewModel.SelectedExpense);
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel viewModel)
            {
                viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _expenseWindow?.Close();
            base.OnUnloaded(e);
        }
    }
}