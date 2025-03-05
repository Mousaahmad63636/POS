using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class CustomerView : UserControl
    {
        public CustomerView()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing Customer View: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // You could also add global exception handling here for any operations specific to this view
    }
}