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



            // Set margins and paddings based on window size

            var scrollViewer = this.Content as Grid;

            if (scrollViewer == null) return;



            var rootGrid = scrollViewer.Children[0] as ScrollViewer;

            if (rootGrid == null) return;



            var contentGrid = rootGrid.Content as Grid;

            if (contentGrid == null) return;



            if (windowWidth >= 1920) // Large screens

            {

                contentGrid.Margin = new Thickness(32);

            }

            else if (windowWidth >= 1366) // Medium screens

            {

                contentGrid.Margin = new Thickness(24);

            }

            else if (windowWidth >= 800) // Small screens

            {

                contentGrid.Margin = new Thickness(16);

            }

            else // Very small screens

            {

                contentGrid.Margin = new Thickness(8);

            }

        }



        private void TransactionPopup_CloseRequested(object sender, RoutedEventArgs e)

        {

            if (DataContext is ViewModels.CustomerDebtViewModel viewModel)

            {

                viewModel.IsTransactionPopupOpen = false;

            }

        }



        private void TransactionPopup_SaveCompleted(object sender, RoutedEventArgs e)

        {

            if (DataContext is ViewModels.CustomerDebtViewModel viewModel)

            {

                viewModel.IsTransactionPopupOpen = false;

            }

        }

    }

}