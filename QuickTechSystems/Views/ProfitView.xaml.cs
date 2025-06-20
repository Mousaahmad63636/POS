using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickTechSystems.WPF.Views
{
    public partial class ProfitView : UserControl
    {
        // Column indices for responsive visibility
        private const int DATE_COLUMN_INDEX = 0;
        private const int TIME_COLUMN_INDEX = 1;
        private const int SALES_COLUMN_INDEX = 2;
        private const int COST_COLUMN_INDEX = 3;
        private const int GROSS_PROFIT_COLUMN_INDEX = 4;
        private const int NET_PROFIT_COLUMN_INDEX = 5;
        private const int MARGIN_COLUMN_INDEX = 6;
        private const int ITEMS_COLUMN_INDEX = 7;

        // References to XAML elements
        private Border summaryPopup;

        public ProfitView()
        {
            InitializeComponent();
            this.Loaded += ProfitView_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void ProfitView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure DataContext is set if using DI
            if (DataContext != null)
            {
                // Any initialization if needed
            }

            // Find and store references to named XAML elements
            FindXamlElements();

            AdjustLayoutForSize();
        }

        private void FindXamlElements()
        {
            // Find the summary popup in the visual tree
            summaryPopup = this.FindName("SummaryPopup") as Border;

            // If not found via FindName, try searching the visual tree
            if (summaryPopup == null)
            {
                summaryPopup = FindVisualChild<Border>(this, "SummaryPopup");
            }

            // If still not found, create it programmatically
            if (summaryPopup == null)
            {
                CreateSummaryPopup();
            }
        }

        // Helper method to find a named element in the visual tree
        private T FindVisualChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            // Create a childCount to prevent infinite recursion if visual tree is malformed
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // If this is the child we're looking for, return it
                if (child is FrameworkElement element && element.Name == childName && child is T typedChild)
                {
                    return typedChild;
                }

                // Otherwise, recurse into its children
                var result = FindVisualChild<T>(child, childName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        // Create the popup programmatically if it wasn't found in XAML
        private void CreateSummaryPopup()
        {
            summaryPopup = new Border
            {
                Name = "SummaryPopup",
                Background = (Brush)System.Windows.Application.Current.Resources["BackdropColor"],
                Visibility = Visibility.Collapsed
            };

            // Create popup content
            var popupContent = new Border
            {
                Background = (Brush)System.Windows.Application.Current.Resources["BackgroundColor"],
                BorderBrush = (Brush)System.Windows.Application.Current.Resources["BorderColor"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                MaxWidth = 900,
                MaxHeight = 600,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Create a layout grid for the popup
            var popupGrid = new Grid
            {
                Margin = new Thickness(24)
            };

            // Create close button
            var closeButton = new Button
            {
                Content = "Close",
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Center,
                MinWidth = 120,
                Margin = new Thickness(0, 24, 0, 0)
            };
            closeButton.Click += ClosePopupButton_Click;

            // Add basic content to the popup
            var titleText = new TextBlock
            {
                Text = "Profit Summary",
                Style = (Style)System.Windows.Application.Current.Resources["HeadlineMedium"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            popupGrid.Children.Add(titleText);
            popupGrid.Children.Add(closeButton);

            // Set up grid rows
            popupGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            popupGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Set grid positions
            Grid.SetRow(titleText, 0);
            Grid.SetRow(closeButton, 1);

            // Assemble the popup
            popupContent.Child = popupGrid;
            summaryPopup.Child = popupContent;

            // Add to the visual tree
            var grid = this.Content as Grid;
            if (grid != null)
            {
                grid.Children.Add(summaryPopup);
            }
            else
            {
                // Fix: Create a container and properly handle the Content conversion
                var container = new Grid();

                // Properly convert the existing content to UIElement or ContentPresenter
                if (this.Content is UIElement contentElement)
                {
                    container.Children.Add(contentElement);
                }
                else if (this.Content != null)
                {
                    // If content is not a UIElement, wrap it in a ContentPresenter
                    var contentPresenter = new ContentPresenter
                    {
                        Content = this.Content
                    };
                    container.Children.Add(contentPresenter);
                }

                container.Children.Add(summaryPopup);
                this.Content = container;
            }
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
            double windowHeight = parentWindow.ActualHeight;

            // Automatically adjust the layout based on the window size
            SetResponsiveLayout(windowWidth, windowHeight);
        }

        private void SetResponsiveLayout(double width, double height)
        {
            // Adaptive margin based on screen size
            double marginSize = CalculateAdaptiveMargin(width);

            // Find ContentGrid by name if it exists
            var contentGrid = this.FindName("ContentGrid") as Grid;
            if (contentGrid != null)
            {
                contentGrid.Margin = new Thickness(marginSize);
            }

            // Determine which data grid columns to show based on available width
            bool showDateColumn = true; // Always show
            bool showTimeColumn = width >= 600;
            bool showSalesColumn = true; // Always show
            bool showCostColumn = width >= 800;
            bool showGrossProfitColumn = true; // Always show
            bool showNetProfitColumn = width >= 900;
            bool showMarginColumn = width >= 1000;
            bool showItemsColumn = width >= 1100;

            // Apply column visibility
            SetColumnVisibility(showDateColumn, showTimeColumn, showSalesColumn, showCostColumn,
                               showGrossProfitColumn, showNetProfitColumn, showMarginColumn, showItemsColumn);

            // Set popup size based on screen dimensions
            if (summaryPopup != null && summaryPopup.Visibility == Visibility.Visible)
            {
                AdjustPopupSize(width, height);
            }
        }

        private double CalculateAdaptiveMargin(double screenWidth)
        {
            // Calculate margin as a percentage of screen width
            double marginPercentage = 0.02; // 2% of screen width
            double calculatedMargin = screenWidth * marginPercentage;

            // Ensure margin stays within reasonable bounds
            return Math.Max(8, Math.Min(32, calculatedMargin));
        }

        private void SetColumnVisibility(bool showDateColumn, bool showTimeColumn, bool showSalesColumn,
                                      bool showCostColumn, bool showGrossProfitColumn, bool showNetProfitColumn,
                                      bool showMarginColumn, bool showItemsColumn)
        {
            // Find DataGrid by name if reference not available
            var profitDetailsDataGrid = this.FindName("ProfitDetailsDataGrid") as DataGrid;
            if (profitDetailsDataGrid == null || profitDetailsDataGrid.Columns.Count == 0)
                return;

            // Safely check if we have enough columns
            if (profitDetailsDataGrid.Columns.Count > DATE_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[DATE_COLUMN_INDEX].Visibility =
                    showDateColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > TIME_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[TIME_COLUMN_INDEX].Visibility =
                    showTimeColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > SALES_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[SALES_COLUMN_INDEX].Visibility =
                    showSalesColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > COST_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[COST_COLUMN_INDEX].Visibility =
                    showCostColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > GROSS_PROFIT_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[GROSS_PROFIT_COLUMN_INDEX].Visibility =
                    showGrossProfitColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > NET_PROFIT_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[NET_PROFIT_COLUMN_INDEX].Visibility =
                    showNetProfitColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > MARGIN_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[MARGIN_COLUMN_INDEX].Visibility =
                    showMarginColumn ? Visibility.Visible : Visibility.Collapsed;

            if (profitDetailsDataGrid.Columns.Count > ITEMS_COLUMN_INDEX)
                profitDetailsDataGrid.Columns[ITEMS_COLUMN_INDEX].Visibility =
                    showItemsColumn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SummaryButton_Click(object sender, RoutedEventArgs e)
        {
            if (summaryPopup != null)
            {
                summaryPopup.Visibility = Visibility.Visible;

                // Adjust popup size based on window dimensions
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    AdjustPopupSize(parentWindow.ActualWidth, parentWindow.ActualHeight);
                }
            }
        }

        private void ClosePopupButton_Click(object sender, RoutedEventArgs e)
        {
            if (summaryPopup != null)
            {
                summaryPopup.Visibility = Visibility.Collapsed;
            }
        }

        private void AdjustPopupSize(double windowWidth, double windowHeight)
        {
            if (summaryPopup == null) return;

            // Find the content border of the popup
            var popupContent = summaryPopup.Child as Border;
            if (popupContent == null) return;

            // Calculate responsive dimensions
            double maxWidth = Math.Min(900, windowWidth * 0.9);
            double maxHeight = Math.Min(600, windowHeight * 0.85);

            popupContent.MaxWidth = maxWidth;
            popupContent.MaxHeight = maxHeight;
        }
    }
}