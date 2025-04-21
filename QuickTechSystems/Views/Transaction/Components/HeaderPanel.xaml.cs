using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class HeaderPanel : UserControl
    {
        public HeaderPanel()
        {
            InitializeComponent();

            // Add event handler for mouse down to close the popup when clicking outside
            this.MouseDown += HeaderPanel_MouseDown;

            // Add unloaded event handler
            this.Unloaded += UserControl_Unloaded;
        }

        private void HeaderPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close customer search dropdown when clicking outside
            if (DataContext is TransactionViewModel viewModel && viewModel.IsCustomerSearchVisible)
            {
                // Check if the click is outside the CustomerSearchContainer
                if (!IsMouseOverControl(CustomerSearchContainer, e.GetPosition(this)))
                {
                    viewModel.IsCustomerSearchVisible = false;
                }
            }
        }

        private bool IsMouseOverControl(UIElement element, Point mousePosition)
        {
            if (element == null) return false;

            // Get the bounds of the element
            var bounds = element.TransformToVisual(this)
                                .TransformBounds(new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height));

            // Check if the mouse position is within the bounds
            return bounds.Contains(mousePosition);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            this.MouseDown -= HeaderPanel_MouseDown;
            this.Unloaded -= UserControl_Unloaded;
        }

        private void CustomerCancelButton_Click(object sender, RoutedEventArgs e)
        {
            SafeExecute(async () => {
                if (CustomerSearchBox != null)
                {
                    CustomerSearchBox.Text = string.Empty;

                    // Get the view model
                    if (DataContext is TransactionViewModel viewModel)
                    {
                        // Execute the clear command
                        if (viewModel.ClearCustomerCommand.CanExecute(null))
                        {
                            viewModel.ClearCustomerCommand.Execute(null);
                        }
                    }
                }
            });
        }

        // Add a general exception handler for async events
        private async void SafeExecute(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UI operation: {ex.Message}");
                MessageBox.Show(
                    $"An unexpected error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}