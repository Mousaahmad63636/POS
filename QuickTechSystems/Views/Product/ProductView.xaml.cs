using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class ProductView : UserControl
    {
        public ProductView()
        {
            InitializeComponent();
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                // Reset all tab buttons to inactive style
                BasicInfoTab.Style = (Style)Resources["TabButtonStyle"];
                PricingTab.Style = (Style)Resources["TabButtonStyle"];
                InventoryTab.Style = (Style)Resources["TabButtonStyle"];

                // Hide all tab content
                BasicInfoContent.Visibility = Visibility.Collapsed;
                PricingContent.Visibility = Visibility.Collapsed;
                InventoryContent.Visibility = Visibility.Collapsed;

                // Set clicked button to active style and show corresponding content
                clickedButton.Style = (Style)Resources["ActiveTabButtonStyle"];

                string tabTag = clickedButton.Tag?.ToString() ?? "";
                switch (tabTag)
                {
                    case "BasicInfo":
                        BasicInfoContent.Visibility = Visibility.Visible;
                        break;
                    case "Pricing":
                        PricingContent.Visibility = Visibility.Visible;
                        break;
                    case "Inventory":
                        InventoryContent.Visibility = Visibility.Visible;
                        break;
                }
            }
        }
    }
}