using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class CustomerDebtView : UserControl
    {
        public CustomerDebtView()
        {
            InitializeComponent();
            this.Loaded += CustomerDebtView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void CustomerDebtView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure DataContext is set properly
            if (DataContext != null)
            {
                // Any initialization needed
            }

            // Adjust layout based on size
            AdjustLayoutForSize();
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustLayoutForSize();
        }

        private void AdjustLayoutForSize()
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            // Get actual window dimensions
            double windowWidth = parentWindow.ActualWidth;

            // Adjust layout based on window width
            if (windowWidth >= 1920) // Large screens
            {
                // Maximum width for the detail panel
                var rightColumn = this.FindName("rightColumn") as ColumnDefinition;
                if (rightColumn != null)
                {
                    rightColumn.Width = new GridLength(450);
                }
            }
            else if (windowWidth >= 1366) // Medium screens
            {
                var rightColumn = this.FindName("rightColumn") as ColumnDefinition;
                if (rightColumn != null)
                {
                    rightColumn.Width = new GridLength(400);
                }
            }
            else if (windowWidth <= 1000) // Small screens
            {
                var rightColumn = this.FindName("rightColumn") as ColumnDefinition;
                if (rightColumn != null)
                {
                    rightColumn.Width = new GridLength(350);
                }
            }
        }
    }
}